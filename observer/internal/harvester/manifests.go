package harvester

import (
	"context"
	"encoding/json"
	"fmt"
	"regexp"

	"k8s.io/apimachinery/pkg/api/errors"

	appsv1 "k8s.io/api/apps/v1"
	corev1 "k8s.io/api/core/v1"
	"sigs.k8s.io/controller-runtime/pkg/client"
)

// ManifestFetcher defines an interface for fetching Kubernetes manifests.
type ManifestFetcher interface {
	GetPodManifest(ctx context.Context, namespace, name string) (string, error)
	GetDeploymentManifest(ctx context.Context, namespace, name string) (string, error)
}

// K8sManifestFetcher implements ManifestFetcher using client-go.
type K8sManifestFetcher struct {
	Client client.Client
}

// NewK8sManifestFetcher creates a new K8sManifestFetcher.
func NewK8sManifestFetcher(client client.Client) *K8sManifestFetcher {
	return &K8sManifestFetcher{Client: client}
}

// GetPodManifest retrieves and serializes a Pod manifest to JSON.
func (f *K8sManifestFetcher) GetPodManifest(ctx context.Context, namespace, name string) (string, error) {
	pod := &corev1.Pod{}
	err := f.Client.Get(ctx, client.ObjectKey{Namespace: namespace, Name: name}, pod)
	if err != nil {
		if errors.IsNotFound(err) {
			return "", fmt.Errorf("pod %s/%s not found", namespace, name)
		}
		return "", fmt.Errorf("failed to get pod %s/%s: %w", namespace, name, err)
	}

	jsonBytes, err := json.MarshalIndent(pod, "", "  ")
	if err != nil {
		return "", fmt.Errorf("failed to marshal pod %s/%s to JSON: %w", namespace, name, err)
	}
	return string(jsonBytes), nil
}

// GetDeploymentManifest retrieves and serializes a Deployment manifest to JSON.
func (f *K8sManifestFetcher) GetDeploymentManifest(ctx context.Context, namespace, name string) (string, error) {
	deployment := &appsv1.Deployment{}
	err := f.Client.Get(ctx, client.ObjectKey{Namespace: namespace, Name: name}, deployment)
	if err != nil {
		if errors.IsNotFound(err) {
			return "", fmt.Errorf("deployment %s/%s not found", namespace, name)
		}
		return "", fmt.Errorf("failed to get deployment %s/%s: %w", namespace, name, err)
	}

	jsonBytes, err := json.MarshalIndent(deployment, "", "  ")
	if err != nil {
		return "", fmt.Errorf("failed to marshal deployment %s/%s to JSON: %w", namespace, name, err)
	}
	return string(jsonBytes), nil
}
// RedactionEngine defines an interface for redacting sensitive data.
type RedactionEngine interface {
	Redact(manifest string) (string, error)
}

// RegexRedactionEngine implements RedactionEngine using regular expressions.
type RegexRedactionEngine struct {
	patterns []*regexp.Regexp
}

// NewRegexRedactionEngine creates a new RegexRedactionEngine with a default set of patterns.
func NewRegexRedactionEngine() (*RegexRedactionEngine, error) {
	patterns := []string{
		`("name":\s*".*?_SECRET.*?",\s*"value":\s*").*?"`,
		`("name":\s*".*?_TOKEN.*?",\s*"value":\s*").*?"`,
		`("name":\s*".*?_KEY.*?",\s*"value":\s*").*?"`,
		`(?i)("name":\s*".*?PASSWORD.*?",\s*"value":\s*").*?"`,
	}

	compiledPatterns := make([]*regexp.Regexp, len(patterns))
	for i, p := range patterns {
		re, err := regexp.Compile(p)
		if err != nil {
			return nil, fmt.Errorf("failed to compile regex pattern: %w", err)
		}
		compiledPatterns[i] = re
	}

	return &RegexRedactionEngine{patterns: compiledPatterns}, nil
}

// Redact applies all registered regex patterns to the manifest.
func (e *RegexRedactionEngine) Redact(manifest string) (string, error) {
	redactedManifest := manifest
	for _, re := range e.patterns {
		redactedManifest = re.ReplaceAllString(redactedManifest, `${1}[REDACTED]"`)
	}
	return redactedManifest, nil
}

// ManifestParser combines fetching and redacting manifests.
type ManifestParser struct {
	Fetcher  ManifestFetcher
	Redactor RedactionEngine
}

// NewManifestParser creates a new ManifestParser.
func NewManifestParser(client client.Client) (*ManifestParser, error) {
	fetcher := NewK8sManifestFetcher(client)
	redactor, err := NewRegexRedactionEngine()
	if err != nil {
		return nil, err
	}
	return &ManifestParser{Fetcher: fetcher, Redactor: redactor}, nil
}

// GetAndRedactPodManifest fetches, redacts, and returns a pod manifest.
func (p *ManifestParser) GetAndRedactPodManifest(ctx context.Context, namespace, name string) (string, error) {
	manifest, err := p.Fetcher.GetPodManifest(ctx, namespace, name)
	if err != nil {
		return "", err
	}
	return p.Redactor.Redact(manifest)
}
