package harvester_test

import (
	"context"
	"strings"
	"testing"

	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	"kube-mind/observer/internal/harvester"
)

type mockManifestFetcher struct {
	getPodManifestFunc      func(ctx context.Context, namespace, name string) (string, error)
	getDeploymentManifestFunc func(ctx context.Context, namespace, name string) (string, error)
}

func (m *mockManifestFetcher) GetPodManifest(ctx context.Context, namespace, name string) (string, error) {
	return m.getPodManifestFunc(ctx, namespace, name)
}

func (m *mockManifestFetcher) GetDeploymentManifest(ctx context.Context, namespace, name string) (string, error) {
	return m.getDeploymentManifestFunc(ctx, namespace, name)
}

func TestRegexRedactionEngine_Redact(t *testing.T) {
	engine, err := harvester.NewRegexRedactionEngine()
	require.NoError(t, err)

	testCases := []struct {
		name             string
		manifest         string
		expectedRedacted string
	}{
		{
			name: "redact secret",
			manifest: `{
				"name": "MY_APP_SECRET",
				"value": "supersecretvalue"
			}`,
			expectedRedacted: `{
				"name": "MY_APP_SECRET",
				"value": "[REDACTED]"
			}`,
		},
		{
			name: "redact token",
			manifest: `{
				"name": "API_TOKEN",
				"value": "token-12345"
			}`,
			expectedRedacted: `{
				"name": "API_TOKEN",
				"value": "[REDACTED]"
			}`,
		},
		{
			name: "redact key",
			manifest: `{
				"name": "PRIVATE_KEY",
				"value": "private-key-data"
			}`,
			expectedRedacted: `{
				"name": "PRIVATE_KEY",
				"value": "[REDACTED]"
			}`,
		},
		{
			name: "redact password",
			manifest: `{
				"name": "DB_PASSWORD",
				"value": "password123"
			}`,
			expectedRedacted: `{
				"name": "DB_PASSWORD",
				"value": "[REDACTED]"
			}`,
		},
		{
			name:             "no redaction needed",
			manifest:         `{"name": "MY_APP_SETTING", "value": "somevalue"}`,
			expectedRedacted: `{"name": "MY_APP_SETTING", "value": "somevalue"}`,
		},
	}

	for _, tt := range testCases {
		t.Run(tt.name, func(t *testing.T) {
			// Normalize JSON strings for comparison
			tt.manifest = strings.ReplaceAll(strings.ReplaceAll(tt.manifest, "\n", ""), "\t", "")
			tt.expectedRedacted = strings.ReplaceAll(strings.ReplaceAll(tt.expectedRedacted, "\n", ""), "\t", "")

			redacted, err := engine.Redact(tt.manifest)
			require.NoError(t, err)
			assert.JSONEq(t, tt.expectedRedacted, redacted)
		})
	}
}

func TestManifestParser_GetAndRedactPodManifest(t *testing.T) {
	ctx := context.Background()
	namespace := "default"
	podName := "test-pod"

	unredactedManifest := `{
		"apiVersion": "v1",
		"kind": "Pod",
		"metadata": {
			"name": "test-pod"
		},
		"spec": {
			"containers": [
				{
					"name": "test-container",
					"image": "nginx",
					"env": [
						{
							"name": "MY_APP_SECRET",
							"value": "supersecretvalue"
						},
						{
							"name": "MY_APP_SETTING",
							"value": "somevalue"
						}
					]
				}
			]
		}
	}`

	redactedManifest := `{
		"apiVersion": "v1",
		"kind": "Pod",
		"metadata": {
			"name": "test-pod"
		},
		"spec": {
			"containers": [
				{
					"name": "test-container",
					"image": "nginx",
					"env": [
						{
							"name": "MY_APP_SECRET",
							"value": "[REDACTED]"
						},
						{
							"name": "MY_APP_SETTING",
							"value": "somevalue"
						}
					]
				}
			]
		}
	}`

	fetcher := &mockManifestFetcher{
		getPodManifestFunc: func(ctx context.Context, namespace, name string) (string, error) {
			return unredactedManifest, nil
		},
	}

	redactor, err := harvester.NewRegexRedactionEngine()
	require.NoError(t, err)

	parser := &harvester.ManifestParser{
		Fetcher:  fetcher,
		Redactor: redactor,
	}

	result, err := parser.GetAndRedactPodManifest(ctx, namespace, podName)
	require.NoError(t, err)
	assert.JSONEq(t, redactedManifest, result)
}
