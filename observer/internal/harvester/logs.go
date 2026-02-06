package harvester

import (
	"context"
	"fmt"
	"io"
	"strings"

	logf "sigs.k8s.io/controller-runtime/pkg/log"

	corev1 "k8s.io/api/core/v1"
	"k8s.io/client-go/kubernetes"
)

// PodLogStreamer defines an interface for streaming pod logs.
type PodLogStreamer interface {
	StreamPodLogs(ctx context.Context, namespace, podName, containerName string, tailLines *int64) (io.ReadCloser, error)
}

// K8sPodLogStreamer implements PodLogStreamer using kubernetes clientset.
type K8sPodLogStreamer struct {
	Clientset *kubernetes.Clientset
}

// StreamPodLogs implements PodLogStreamer for actual Kubernetes API calls.
func (s *K8sPodLogStreamer) StreamPodLogs(ctx context.Context, namespace, podName, containerName string, tailLines *int64) (io.ReadCloser, error) {
	podLogOptions := &corev1.PodLogOptions{
		Container: containerName,
		TailLines: tailLines,
	}
	req := s.Clientset.CoreV1().Pods(namespace).GetLogs(podName, podLogOptions)
	return req.Stream(ctx)
}

// LogAggregator defines the interface for log aggregation.
type LogAggregator interface {
	GetLogs(ctx context.Context, namespace, podName, containerName string, tailLines int64) (string, error)
}

// K8sLogAggregator implements LogAggregator using a PodLogStreamer.
type K8sLogAggregator struct {
	Streamer PodLogStreamer
}

// NewK8sLogAggregator creates a new K8sLogAggregator with a default K8sPodLogStreamer.
// It directly accepts a kubernetes.Clientset to avoid re-creating it from config.
func NewK8sLogAggregator(clientset *kubernetes.Clientset) *K8sLogAggregator {
	return &K8sLogAggregator{
		Streamer: &K8sPodLogStreamer{Clientset: clientset},
	}
}

func NewK8sLogAggregatorWithStreamer(streamer PodLogStreamer) *K8sLogAggregator {
	return &K8sLogAggregator{
		Streamer: streamer,
	}
}

// GetLogs retrieves the last 'tailLines' of logs for a specific container in a pod.
func (a *K8sLogAggregator) GetLogs(ctx context.Context, namespace, podName, containerName string, tailLines int64) (string, error) {
	podLogs, err := a.Streamer.StreamPodLogs(ctx, namespace, podName, containerName, &tailLines)
	if err != nil {
		return "", fmt.Errorf("error in opening stream: %w", err)
	}
	defer func() {
		log := logf.FromContext(ctx)
		if closeErr := podLogs.Close(); closeErr != nil {
			log.Error(closeErr, "error closing pod logs stream")
		}
	}()

	buf := new(strings.Builder)
	if _, err = io.Copy(buf, podLogs); err != nil {
		return "", fmt.Errorf("error in copy from podLogs to buffer: %w", err)
	}

	return buf.String(), nil
}
