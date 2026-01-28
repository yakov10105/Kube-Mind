package harvester_test

import (
	"context"
	"errors"
	"strings"
	"testing"

	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	"kube-mind/observer/internal/harvester"
)

type mockManifestFetcher struct {
	getPodManifestFunc        func(ctx context.Context, namespace, name string) (string, error)
	getDeploymentManifestFunc func(ctx context.Context, namespace, name string) (string, error)
}

func (m *mockManifestFetcher) GetPodManifest(ctx context.Context, namespace, name string) (string, error) {
	if m.getPodManifestFunc != nil {
		return m.getPodManifestFunc(ctx, namespace, name)
	}
	return "", errors.New("GetPodManifest not implemented")
}
func (m *mockManifestFetcher) GetDeploymentManifest(ctx context.Context, namespace, name string) (string, error) {
	if m.getDeploymentManifestFunc != nil {
		return m.getDeploymentManifestFunc(ctx, namespace, name)
	}
	return "", errors.New("GetDeploymentManifest not implemented")
}

func TestRegexRedactionEngine_Redact(t *testing.T) {
	t.Parallel()
	engine, err := harvester.NewRegexRedactionEngine()
	require.NoError(t, err)

	testCases := []struct {
		name             string
		manifest         string
		expectedRedacted string
	}{
		{
			name:             "redact secret",
			manifest:         `{"name": "MY_APP_SECRET", "value": "supersecretvalue"}`,
			expectedRedacted: `{"name": "MY_APP_SECRET", "value": "[REDACTED]"}`,
		},
		{
			name:             "redact token",
			manifest:         `{"name": "API_TOKEN", "value": "token-12345"}`,
			expectedRedacted: `{"name": "API_TOKEN", "value": "[REDACTED]"}`,
		},
		{
			name:             "redact key",
			manifest:         `{"name": "PRIVATE_KEY", "value": "private-key-data"}`,
			expectedRedacted: `{"name": "PRIVATE_KEY", "value": "[REDACTED]"}`,
		},
		{
			name:             "redact password with different casing",
			manifest:         `{"name": "db_password", "value": "password123"}`,
			expectedRedacted: `{"name": "db_password", "value": "[REDACTED]"}`,
		},
		{
			name:             "no redaction needed",
			manifest:         `{"name": "MY_APP_SETTING", "value": "somevalue"}`,
			expectedRedacted: `{"name": "MY_APP_SETTING", "value": "somevalue"}`,
		},
	}

	for _, tc := range testCases {
		tc := tc
		t.Run(tc.name, func(t *testing.T) {
			t.Parallel()

			// Normalize JSON strings by removing whitespace
			manifest := strings.ReplaceAll(tc.manifest, " ", "")
			expected := strings.ReplaceAll(tc.expectedRedacted, " ", "")

			redacted, err := engine.Redact(manifest)
			require.NoError(t, err)
			assert.JSONEq(t, expected, redacted)
		})
	}
}

func TestManifestParser_GetAndRedactPodManifest(t *testing.T) {
	t.Parallel()
	ctx := context.Background()

	unredactedManifest := `{"env": [{"name": "MY_APP_SECRET", "value": "supersecretvalue"}]}`
	redactedManifest := `{"env": [{"name": "MY_APP_SECRET", "value": "[REDACTED]"}]}`

	testCases := []struct {
		name                string
		fetcher             harvester.ManifestFetcher
		expectErr           bool
		expectedErrContains string
		expectedManifest    string
	}{
		{
			name: "Successful fetch and redact",
			fetcher: &mockManifestFetcher{
				getPodManifestFunc: func(ctx context.Context, namespace, name string) (string, error) {
					return unredactedManifest, nil
				},
			},
			expectErr:        false,
			expectedManifest: redactedManifest,
		},
		{
			name: "Error on fetch",
			fetcher: &mockManifestFetcher{
				getPodManifestFunc: func(ctx context.Context, namespace, name string) (string, error) {
					return "", errors.New("fetch error")
				},
			},
			expectErr:           true,
			expectedErrContains: "fetch error",
		},
	}

	for _, tc := range testCases {
		tc := tc
		t.Run(tc.name, func(t *testing.T) {
			t.Parallel()

			redactor, err := harvester.NewRegexRedactionEngine()
			require.NoError(t, err)

			parser := &harvester.ManifestParser{
				Fetcher:  tc.fetcher,
				Redactor: redactor,
			}

			result, err := parser.GetAndRedactPodManifest(ctx, "default", "test-pod")

			if tc.expectErr {
				require.Error(t, err)
				assert.Contains(t, err.Error(), tc.expectedErrContains)
			} else {
				require.NoError(t, err)
				assert.JSONEq(t, tc.expectedManifest, result)
			}
		})
	}
}
