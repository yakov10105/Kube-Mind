/*
Copyright 2026.

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
*/

package main

import (
	"crypto/tls"
	"flag"
	"os"

	_ "k8s.io/client-go/plugin/pkg/client/auth"

	"go.uber.org/zap/zapcore"
	"k8s.io/apimachinery/pkg/runtime"
	utilruntime "k8s.io/apimachinery/pkg/util/runtime"
	clientgoscheme "k8s.io/client-go/kubernetes/scheme"
	"k8s.io/client-go/kubernetes"
	ctrl "sigs.k8s.io/controller-runtime"
	"sigs.k8s.io/controller-runtime/pkg/healthz"
	"sigs.k8s.io/controller-runtime/pkg/log/zap"
	"sigs.k8s.io/controller-runtime/pkg/metrics/filters"
	metricsserver "sigs.k8s.io/controller-runtime/pkg/metrics/server"
	"sigs.k8s.io/controller-runtime/pkg/webhook"

	"kube-mind/observer/internal/comms"
	observerconfig "kube-mind/observer/internal/config"
	"kube-mind/observer/internal/controller"
	"kube-mind/observer/internal/harvester"
	// +kubebuilder:scaffold:imports
)

var (
	scheme   = runtime.NewScheme()
	setupLog = ctrl.Log.WithName("setup")
)

func init() {
	utilruntime.Must(clientgoscheme.AddToScheme(scheme))

	// +kubebuilder:scaffold:scheme
}

// nolint:gocyclo
func main() {
	var metricsAddr string
	var metricsCertPath, metricsCertName, metricsCertKey string
	var webhookCertPath, webhookCertName, webhookCertKey string
	var enableLeaderElection bool
	var probeAddr string
	var secureMetrics bool
	var enableHTTP2 bool
	var grpcServerAddress string
	var grpcCaCertPath, grpcClientCertPath, grpcClientKeyPath string
	var grpcInsecure bool
	var tlsOpts []func(*tls.Config)
	flag.StringVar(&metricsAddr, "metrics-bind-address", "0", "The address the metrics endpoint binds to. "+
		"Use :8443 for HTTPS or :8080 for HTTP, or leave as 0 to disable the metrics service.")
	flag.StringVar(&probeAddr, "health-probe-bind-address", ":8081", "The address the probe endpoint binds to.")
	flag.BoolVar(&enableLeaderElection, "leader-elect", false,
		"Enable leader election for controller manager. "+
			"Enabling this will ensure there is only one active controller manager.")
	flag.BoolVar(&secureMetrics, "metrics-secure", true,
		"If set, the metrics endpoint is served securely via HTTPS. Use --metrics-secure=false to use HTTP instead.")
	flag.StringVar(&webhookCertPath, "webhook-cert-path", "", "The directory that contains the webhook certificate.")
	flag.StringVar(&webhookCertName, "webhook-cert-name", "tls.crt", "The name of the webhook certificate file.")
	flag.StringVar(&webhookCertKey, "webhook-cert-key", "tls.key", "The name of the webhook key file.")
	flag.StringVar(&metricsCertPath, "metrics-cert-path", "",
		"The directory that contains the metrics server certificate.")
	flag.StringVar(&metricsCertName, "metrics-cert-name", "tls.crt", "The name of the metrics server certificate file.")
	flag.StringVar(&metricsCertKey, "metrics-cert-key", "tls.key", "The name of the metrics server key file.")
	flag.BoolVar(&enableHTTP2, "enable-http2", false,
		"If set, HTTP/2 will be enabled for the metrics and webhook servers")
	flag.StringVar(&grpcServerAddress, "grpc-server-address", "localhost:50051", "The address of the gRPC Brain server.")
	flag.StringVar(&grpcCaCertPath, "grpc-ca-cert", "", "Path to the gRPC CA certificate.")
	flag.StringVar(&grpcClientCertPath, "grpc-client-cert", "", "Path to the gRPC client certificate.")
	flag.StringVar(&grpcClientKeyPath, "grpc-client-key", "", "Path to the gRPC client key.")
	flag.BoolVar(&grpcInsecure, "grpc-insecure", false, "If set, connect to the gRPC server without mTLS (for local development).")
	opts := zap.Options{
		Development: true,
	}
	opts.BindFlags(flag.CommandLine)
	flag.Parse()

	cfg, err := observerconfig.LoadConfig()
	if err != nil {
		setupLog.Error(err, "unable to load controller configuration")
		os.Exit(1)
	}

	switch cfg.LogLevel {
	case "debug":
		opts.Level = zapcore.DebugLevel
	case "info":
		opts.Level = zapcore.InfoLevel
	case "error":
		opts.Level = zapcore.ErrorLevel
	default:
		opts.Level = zapcore.InfoLevel
	}
	ctrl.SetLogger(zap.New(zap.UseFlagOptions(&opts)))

	setupLog.Info("Loaded configuration", "logLevel", cfg.LogLevel, "debounceTTLSeconds", cfg.DebounceTTLSeconds)

	disableHTTP2 := func(c *tls.Config) {
		setupLog.Info("disabling http/2")
		c.NextProtos = []string{"http/1.1"}
	}

	if !enableHTTP2 {
		tlsOpts = append(tlsOpts, disableHTTP2)
	}

	webhookTLSOpts := tlsOpts
	webhookServerOptions := webhook.Options{
		TLSOpts: webhookTLSOpts,
	}

	if len(webhookCertPath) > 0 {
		setupLog.Info("Initializing webhook certificate watcher using provided certificates",
			"webhook-cert-path", webhookCertPath, "webhook-cert-name", webhookCertName, "webhook-cert-key", webhookCertKey)

		webhookServerOptions.CertDir = webhookCertPath
		webhookServerOptions.CertName = webhookCertName
		webhookServerOptions.KeyName = webhookCertKey
	}

	webhookServer := webhook.NewServer(webhookServerOptions)

	metricsServerOptions := metricsserver.Options{
		BindAddress:   metricsAddr,
		SecureServing: secureMetrics,
		TLSOpts:       tlsOpts,
	}

	if secureMetrics {
		metricsServerOptions.FilterProvider = filters.WithAuthenticationAndAuthorization
	}

	if len(metricsCertPath) > 0 {
		setupLog.Info("Initializing metrics certificate watcher using provided certificates",
			"metrics-cert-path", metricsCertPath, "metrics-cert-name", metricsCertName, "metrics-cert-key", metricsCertKey)

		metricsServerOptions.CertDir = metricsCertPath
		metricsServerOptions.CertName = metricsCertName
		metricsServerOptions.KeyName = metricsCertKey
	}

	mgr, err := ctrl.NewManager(ctrl.GetConfigOrDie(), ctrl.Options{
		Scheme:                     scheme,
		Metrics:                    metricsServerOptions,
		WebhookServer:              webhookServer,
		HealthProbeBindAddress:     probeAddr,
		LeaderElection:             enableLeaderElection,
		LeaderElectionID:           cfg.LeaderElectionID,
		LeaderElectionResourceLock: cfg.LeaderElectionResourceLock,
		LeaderElectionNamespace:    cfg.LeaderElectionNamespace,
		LeaseDuration:              &cfg.LeaderElectionLeaseDuration,
		RenewDeadline:              &cfg.LeaderElectionRenewDeadline,
		RetryPeriod:                &cfg.LeaderElectionRetryPeriod,
	})
	if err != nil {
		setupLog.Error(err, "unable to start manager")
		os.Exit(1)
	}

	restConfig := ctrl.GetConfigOrDie()
	clientset, err := kubernetes.NewForConfig(restConfig)
	if err != nil {
		setupLog.Error(err, "unable to create kubernetes clientset")
		os.Exit(1)
	}
	logAggregator := harvester.NewK8sLogAggregator(clientset)

	manifestParser, err := harvester.NewManifestParser(mgr.GetClient())
	if err != nil {
		setupLog.Error(err, "unable to create manifest parser")
		os.Exit(1)
	}
	incidentCache := harvester.NewGoCacheIntelligenceCache(cfg.DebounceTTLSeconds, cfg.DebounceTTLSeconds/2)

	grpcClient, err := comms.NewBrainGrpcClient(ctrl.SetupSignalHandler(), grpcServerAddress, grpcInsecure, grpcCaCertPath, grpcClientCertPath, grpcClientKeyPath)
	if err != nil {
		setupLog.Error(err, "unable to create gRPC client")
		os.Exit(1)
	}
	defer func() {
		if err := grpcClient.Close(); err != nil {
			setupLog.Error(err, "failed to close gRPC client")
		}
	}()

	if err = (&controller.PodReconciler{
		Client:         mgr.GetClient(),
		Scheme:         mgr.GetScheme(),
		LogAggregator:  logAggregator,
		ManifestParser: manifestParser,
		IncidentCache:  incidentCache,
		GrpcClient:     grpcClient,
		Config:         cfg,
	}).SetupWithManager(mgr); err != nil {
		setupLog.Error(err, "unable to create controller", "controller", "Pod")
		os.Exit(1)
	}
	// +kubebuilder:scaffold:builder

	if err := mgr.AddHealthzCheck("healthz", healthz.Ping); err != nil {
		setupLog.Error(err, "unable to set up health check")
		os.Exit(1)
	}
	if err := mgr.AddReadyzCheck("readyz", healthz.Ping); err != nil {
		setupLog.Error(err, "unable to set up ready check")
		os.Exit(1)
	}

	setupLog.Info("starting manager")
	if err := mgr.Start(ctrl.SetupSignalHandler()); err != nil {
		setupLog.Error(err, "problem running manager")
		os.Exit(1)
	}
}
