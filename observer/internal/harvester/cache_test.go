package harvester_test

import (
	"testing"
	"time"

	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	go_cache "github.com/patrickmn/go-cache"

	"kube-mind/observer/internal/harvester"
)

func TestGoCacheIntelligenceCache(t *testing.T) {
	t.Parallel()

	testCases := []struct {
		name              string
		key               string
		value             interface{}
		ttl               time.Duration
		defaultExpiration time.Duration
		cleanupInterval   time.Duration
		expectFound       bool
		waitForExpiration bool
	}{
		{
			name:              "Set and Get item with default expiration",
			key:               "key1",
			value:             "value1",
			ttl:               go_cache.DefaultExpiration,
			defaultExpiration: 100 * time.Millisecond,
			cleanupInterval:   10 * time.Millisecond,
			expectFound:       true,
		},
		{
			name:              "Item expires after TTL",
			key:               "key2",
			value:             "value2",
			ttl:               50 * time.Millisecond,
			defaultExpiration: 1 * time.Minute,
			cleanupInterval:   10 * time.Millisecond,
			expectFound:       false,
			waitForExpiration: true,
		},
		{
			name:              "Item with no expiration",
			key:               "key3",
			value:             42,
			ttl:               go_cache.NoExpiration,
			defaultExpiration: 50 * time.Millisecond,
			cleanupInterval:   10 * time.Millisecond,
			expectFound:       true,
		},
		{
			name:              "Get non-existent item",
			key:               "non-existent",
			value:             nil,
			ttl:               go_cache.DefaultExpiration,
			defaultExpiration: 1 * time.Minute,
			cleanupInterval:   10 * time.Millisecond,
			expectFound:       false,
		},
	}

	for _, tc := range testCases {
		tc := tc
		t.Run(tc.name, func(t *testing.T) {
			t.Parallel()

			cache := harvester.NewGoCacheIntelligenceCache(tc.defaultExpiration, tc.cleanupInterval)

			// Add item unless we are testing for a non-existent key
			if tc.value != nil {
				cache.AddOrUpdate(tc.key, tc.value, tc.ttl)
			}

			if tc.waitForExpiration {
				time.Sleep(tc.ttl + tc.cleanupInterval + 10*time.Millisecond)
			}

			retrieved, found := cache.Get(tc.key)

			if tc.expectFound {
				require.True(t, found)
				assert.Equal(t, tc.value, retrieved)
			} else {
				assert.False(t, found)
			}
		})
	}
}