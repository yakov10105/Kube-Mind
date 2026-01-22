package harvester_test

import (
	"testing"
	"time"

	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"

	go_cache "github.com/patrickmn/go-cache" // Alias to avoid conflict with local 'cache' variable

	"kube-mind/observer/internal/harvester"
)

func TestGoCacheIntelligenceCache_AddOrUpdate_Get(t *testing.T) {
	defaultExpiration := 50 * time.Millisecond
	cleanupInterval := 10 * time.Millisecond
	cache := harvester.NewGoCacheIntelligenceCache(defaultExpiration, cleanupInterval)

	key := "test-key"
	value := "test-value"

	// Test AddOrUpdate and Get
	cache.AddOrUpdate(key, value, go_cache.DefaultExpiration)
	retrieved, found := cache.Get(key)
	require.True(t, found)
	assert.Equal(t, value, retrieved)

	// Test item expiration
	time.Sleep(defaultExpiration + cleanupInterval + (10 * time.Millisecond)) // Wait for item to expire and cleanup to run
	_, found = cache.Get(key)
	assert.False(t, found)

	// Test updating an item
	newValue := "new-test-value"
	cache.AddOrUpdate(key, "original-value", go_cache.DefaultExpiration)
	cache.AddOrUpdate(key, newValue, go_cache.NoExpiration)
	retrieved, found = cache.Get(key)
	require.True(t, found)
	assert.Equal(t, newValue, retrieved)
}

func TestGoCacheIntelligenceCache_NoExpiration(t *testing.T) {
	cache := harvester.NewGoCacheIntelligenceCache(go_cache.NoExpiration, 0)

	key := "permanent-key"
	value := "permanent-value"

	cache.AddOrUpdate(key, value, go_cache.NoExpiration)

	// Should not expire
	time.Sleep(100 * time.Millisecond) // A short sleep to ensure no accidental expiration
	retrieved, found := cache.Get(key)
	require.True(t, found)
	assert.Equal(t, value, retrieved)
}
