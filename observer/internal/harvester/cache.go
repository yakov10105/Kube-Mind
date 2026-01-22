package harvester

import (
	"time"

	"github.com/patrickmn/go-cache"
)

// IntelligenceCache defines the interface for an in-memory cache to debounce incidents.
type IntelligenceCache interface {
	AddOrUpdate(key string, obj interface{}, ttl time.Duration)
	Get(key string) (interface{}, bool)
}

// GoCacheIntelligenceCache implements IntelligenceCache using go-cache.
type GoCacheIntelligenceCache struct {
	cache *cache.Cache
}

// NewGoCacheIntelligenceCache creates a new GoCacheIntelligenceCache.
func NewGoCacheIntelligenceCache(defaultExpiration, cleanupInterval time.Duration) *GoCacheIntelligenceCache {
	return &GoCacheIntelligenceCache{
		cache: cache.New(defaultExpiration, cleanupInterval),
	}
}

// AddOrUpdate adds or updates an item in the cache with a specific TTL.
func (c *GoCacheIntelligenceCache) AddOrUpdate(key string, obj interface{}, ttl time.Duration) {
	c.cache.Set(key, obj, ttl)
}

// Get retrieves an item from the cache.
func (c *GoCacheIntelligenceCache) Get(key string) (interface{}, bool) {
	return c.cache.Get(key)
}
