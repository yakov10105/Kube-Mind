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

func TestK8sLogAggregator_GetLogs_TableDriven(t *testing.T) {
	t.Parallel()
	ctx := context.Background()

	testCases := []struct {
		name                string
		mockStream          func() (io.ReadCloser, error)
		expectedLogs        string
		expectErr           bool
		expectedErrContains string
	}{
		{
			name: "Successful log retrieval",
			mockStream: func() (io.ReadCloser, error) {
				return io.NopCloser(strings.NewReader("line 1\nline 2\nline 3")), nil
			},
			expectedLogs: "line 1\nline 2\nline 3",
			expectErr:    false,
		},
		{
			name: "Error opening stream",
			mockStream: func() (io.ReadCloser, error) {
				return nil, errors.New("stream error")
			},
			expectErr:           true,
			expectedErrContains: "error in opening stream",
		},
		{
			name: "Empty logs",
			mockStream: func() (io.ReadCloser, error) {
				return io.NopCloser(strings.NewReader("")), nil
			},
			expectedLogs: "",
			expectErr:    false,
		},
	}

	for _, tc := range testCases {
		tc := tc
		t.Run(tc.name, func(t *testing.T) {
			t.Parallel()

			mockStreamer := &mockPodLogStreamer{
				streamFunc: func(ctx context.Context, namespace, podName, containerName string, tailLines *int64) (io.ReadCloser, error) {
					return tc.mockStream()
				},
			}

			aggregator := harvester.NewK8sLogAggregatorWithStreamer(mockStreamer)

			logs, err := aggregator.GetLogs(ctx, "default", "test-pod", "test-container", 100)

			if tc.expectErr {
				require.Error(t, err)
				assert.Contains(t, err.Error(), tc.expectedErrContains)
			} else {
				require.NoError(t, err)
				assert.Equal(t, tc.expectedLogs, logs)
			}
		})
	}
}

// mockPodLogStreamer is a mock implementation of harvester.PodLogStreamer for testing.
type mockPodLogStreamer struct {
	streamFunc func(ctx context.Context, namespace, podName, containerName string, tailLines *int64) (io.ReadCloser, error)
}

func (m *mockPodLogStreamer) StreamPodLogs(ctx context.Context, namespace, podName, containerName string, tailLines *int64) (io.ReadCloser, error) {
	if m.streamFunc != nil {
		return m.streamFunc(ctx, namespace, podName, containerName, tailLines)
	}
	return nil, errors.New("streamFunc not implemented")
}
