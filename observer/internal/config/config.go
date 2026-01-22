package config

import (
	"os"
	"strconv"
	"time"
)

// ControllerConfig holds the configuration for the observer controller.
type ControllerConfig struct {
	LogLevel           string
	DebounceTTLSeconds time.Duration
}

// LoadConfig loads configuration from environment variables.
func LoadConfig() (*ControllerConfig, error) {
	logLevel := os.Getenv("LOG_LEVEL")
	if logLevel == "" {
		logLevel = "info"
	}

	debounceTTLStr := os.Getenv("DEBOUNCE_TTL_SECONDS")
	debounceTTL, err := strconv.Atoi(debounceTTLStr)
	if err != nil || debounceTTL <= 0 {
		debounceTTL = 300 // Default to 300 seconds (5 minutes)
	}

	return &ControllerConfig{
		LogLevel:           logLevel,
		DebounceTTLSeconds: time.Duration(debounceTTL) * time.Second,
	}, nil
}
