package harvester_test

import (
	"context"
	"errors"
	"io"
	"strings"
	"testing"

	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
	"kube-mind/observer/internal/harvester"
)

// MockPodLogStreamer is a mock implementation of harvester.PodLogStreamer.
type MockPodLogStreamer struct {
	mockStream func(ctx context.Context, namespace, podName, containerName string, tailLines *int64) (io.ReadCloser, error)
}

// StreamPodLogs implements the PodLogStreamer interface.
func (m *MockPodLogStreamer) StreamPodLogs(ctx context.Context, namespace, podName, containerName string, tailLines *int64) (io.ReadCloser, error) {
	if m.mockStream != nil {
		return m.mockStream(ctx, namespace, podName, containerName, tailLines)
	}
	return nil, errors.New("StreamPodLogs not implemented in mock")
}

func TestK8sLogAggregator_GetLogs(t *testing.T) {
	ctx := context.TODO()
	namespace := "default"
	podName := "test-pod"
	containerName := "test-container"
	tailLines := int64(200)

	tests := []struct {
		name         string
		mockStream   func(ctx context.Context, namespace, podName, containerName string, tailLines *int64) (io.ReadCloser, error)
		expectedLogs string
		expectedErr  string
	}{
		{
			name: "successful log retrieval",
			mockStream: func(ctx context.Context, namespace, podName, containerName string, tailLines *int64) (io.ReadCloser, error) {
				return io.NopCloser(strings.NewReader("line 1\nline 2\nline 3")), nil
			},
			expectedLogs: "line 1\nline 2\nline 3",
		},
		{
			name: "error opening stream",
			mockStream: func(ctx context.Context, namespace, podName, containerName string, tailLines *int64) (io.ReadCloser, error) {
				return nil, errors.New("stream error")
			},
			expectedErr: "error in opening stream: stream error",
		},
		{
			name: "empty logs",
			mockStream: func(ctx context.Context, namespace, podName, containerName string, tailLines *int64) (io.ReadCloser, error) {
				return io.NopCloser(strings.NewReader("")), nil
			},
			expectedLogs: "",
		},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			aggregator := &harvester.K8sLogAggregator{
				Streamer: &MockPodLogStreamer{mockStream: tt.mockStream},
			}

			logs, err := aggregator.GetLogs(ctx, namespace, podName, containerName, tailLines)

			if tt.expectedErr != "" {
				require.Error(t, err)
				assert.Contains(t, err.Error(), tt.expectedErr)
				assert.Empty(t, logs)
			} else {
				require.NoError(t, err)
				assert.Equal(t, tt.expectedLogs, logs)
			}
		})
	}
}