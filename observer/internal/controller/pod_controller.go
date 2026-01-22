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

package controller

import (
	"context"
	"fmt"
	"time"

	appsv1 "k8s.io/api/apps/v1"
	corev1 "k8s.io/api/core/v1"
	"k8s.io/apimachinery/pkg/runtime"
	ctrl "sigs.k8s.io/controller-runtime"
	"sigs.k8s.io/controller-runtime/pkg/client"
	logf "sigs.k8s.io/controller-runtime/pkg/log"

	"google.golang.org/protobuf/types/known/timestamppb"

	"kube-mind/observer/internal/comms"
	"kube-mind/observer/internal/config"
	"kube-mind/observer/internal/harvester"
	pb "kube-mind/observer/proto"
)

// PodReconciler reconciles a Pod object
type PodReconciler struct {
	client.Client
	Scheme *runtime.Scheme
	LogAggregator   harvester.LogAggregator
	ManifestParser  *harvester.ManifestParser
	IncidentCache   harvester.IntelligenceCache
	GrpcClient      comms.GrpcClient
	Config          *config.ControllerConfig
}

// +kubebuilder:rbac:groups=core,resources=pods,verbs=get;list;watch;create;update;patch;delete
// +kubebuilder:rbac:groups=core,resources=pods/status,verbs=get;update;patch
// +kubebuilder:rbac:groups=core,resources=pods/finalizers,verbs=update

// Reconcile is part of the main kubernetes reconciliation loop which aims to
// move the current state of the cluster closer to the desired state.
func (r *PodReconciler) Reconcile(ctx context.Context, req ctrl.Request) (ctrl.Result, error) {
	log := logf.FromContext(ctx)

	pod := &corev1.Pod{}
	if err := r.Get(ctx, req.NamespacedName, pod); err != nil {
		return ctrl.Result{}, client.IgnoreNotFound(err)
	}

	// Filter for non-Running states, specifically CrashLoopBackOff
	for _, containerStatus := range pod.Status.ContainerStatuses {
		if containerStatus.State.Waiting != nil && containerStatus.State.Waiting.Reason == "CrashLoopBackOff" {
			log.Info("Pod entered CrashLoopBackOff", "pod", pod.Name, "namespace", pod.Namespace, "container", containerStatus.Name)

			incidentKey := fmt.Sprintf("%s/%s/%s", pod.Namespace, pod.Name, containerStatus.Name)
			if _, found := r.IncidentCache.Get(incidentKey); found {
				log.Info("Incident debounced", "key", incidentKey)
				return ctrl.Result{}, nil // Debounce repeated incidents
			}

			// Add to cache to debounce future events for this incident
			r.IncidentCache.AddOrUpdate(incidentKey, true, r.Config.DebounceTTLSeconds)

			// Harvest logs
			logs, err := r.LogAggregator.GetLogs(ctx, pod.Namespace, pod.Name, containerStatus.Name, 200) // Last 200 lines
			if err != nil {
				log.Error(err, "failed to get pod logs", "pod", pod.Name, "container", containerStatus.Name)
				return ctrl.Result{}, err
			}

			// Harvest and redact Pod manifest
			podManifest, err := r.ManifestParser.GetAndRedactPodManifest(ctx, pod.Namespace, pod.Name)
			if err != nil {
				log.Error(err, "failed to get and redact pod manifest", "pod", pod.Name)
				return ctrl.Result{}, err
			}

			// Try to find owning deployment and get its manifest
			var deploymentManifest string
			for _, ownerRef := range pod.OwnerReferences {
				if ownerRef.Kind == "ReplicaSet" {
					replicaset := &appsv1.ReplicaSet{}
					if err := r.Get(ctx, client.ObjectKey{Namespace: pod.Namespace, Name: ownerRef.Name}, replicaset); err != nil {
						log.Error(err, "failed to get ReplicaSet", "name", ownerRef.Name)
						continue
					}
					for _, rsOwnerRef := range replicaset.OwnerReferences {
						if rsOwnerRef.Kind == "Deployment" {
							deploymentManifest, err = r.ManifestParser.Fetcher.GetDeploymentManifest(ctx, pod.Namespace, rsOwnerRef.Name)
							if err != nil {
								log.Error(err, "failed to get deployment manifest", "name", rsOwnerRef.Name)
							}
							break
						}
					}
				}
				if deploymentManifest != "" {
					break
				}
			}

			// Create IncidentContext payload
			incidentContext := &pb.IncidentContext{
				IncidentId:             fmt.Sprintf("%s-%s-%s-%d", pod.Name, containerStatus.Name, containerStatus.State.Waiting.Reason, time.Now().Unix()),
				PodName:                pod.Name,
				PodNamespace:           pod.Namespace,
				FailureReason:          containerStatus.State.Waiting.Reason,
				Logs:                   logs,
				PodManifestJson:        podManifest,
				DeploymentManifestJson: deploymentManifest,
				Timestamp:              timestamppb.Now(),
			}

			// Stream incident to the Brain
			if err := r.GrpcClient.StreamIncident(ctx, incidentContext); err != nil {
				log.Error(err, "failed to stream incident to Brain", "incidentID", incidentContext.IncidentId)
				return ctrl.Result{}, err
			}

			log.Info("Incident streamed to Brain", "incidentID", incidentContext.IncidentId)
		}
	}

	return ctrl.Result{}, nil
}

// SetupWithManager sets up the controller with the Manager.
func (r *PodReconciler) SetupWithManager(mgr ctrl.Manager) error {
	return ctrl.NewControllerManagedBy(mgr).
		For(&corev1.Pod{}).
		Named("pod").
		Complete(r)
}