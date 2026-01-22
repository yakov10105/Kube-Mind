package config

import (
	"os"
	"time"
)

// ControllerConfig holds the configuration for the observer controller.
type ControllerConfig struct {
	LogLevel           string
	DebounceTTLSeconds time.Duration
	LeaderElectionNamespace      string
	LeaderElectionID             string
	LeaderElectionResourceLock   string
	LeaderElectionLeaseDuration  time.Duration
	LeaderElectionRenewDeadline  time.Duration
	LeaderElectionRetryPeriod    time.Duration
}

// LoadConfig loads configuration from environment variables.
func LoadConfig() (*ControllerConfig, error) {
	logLevel := os.Getenv("LOG_LEVEL")
	if logLevel == "" {
		logLevel = "info"
	}

	debounceTTLStr := os.Getenv("DEBOUNCE_TTL_SECONDS")
	debounceTTL, err := time.ParseDuration(debounceTTLStr + "s") // Assume seconds if not specified
	if err != nil || debounceTTL <= 0 {
		debounceTTL = 300 * time.Second // Default to 300 seconds (5 minutes)
	}

	leaderElectionNamespace := os.Getenv("LEADER_ELECTION_NAMESPACE")
	if leaderElectionNamespace == "" {
		leaderElectionNamespace = "default"
	}

	leaderElectionID := os.Getenv("LEADER_ELECTION_ID")
	if leaderElectionID == "" {
		leaderElectionID = "19767522.tutorial.kubebuilder.io"
	}

	leaderElectionResourceLock := os.Getenv("LEADER_ELECTION_RESOURCE_LOCK")
	if leaderElectionResourceLock == "" {
		leaderElectionResourceLock = "leases"
	}

	leaseDurationStr := os.Getenv("LEADER_ELECTION_LEASE_DURATION")
	leaseDuration, err := time.ParseDuration(leaseDurationStr)
	if err != nil || leaseDuration <= 0 {
		leaseDuration = 15 * time.Second // Default to 15 seconds
	}

	renewDeadlineStr := os.Getenv("LEADER_ELECTION_RENEW_DEADLINE")
	renewDeadline, err := time.ParseDuration(renewDeadlineStr)
	if err != nil || renewDeadline <= 0 {
		renewDeadline = 10 * time.Second // Default to 10 seconds
	}

	retryPeriodStr := os.Getenv("LEADER_ELECTION_RETRY_PERIOD")
	retryPeriod, err := time.ParseDuration(retryPeriodStr)
	if err != nil || retryPeriod <= 0 {
		retryPeriod = 2 * time.Second // Default to 2 seconds
	}

	return &ControllerConfig{
		LogLevel:           logLevel,
		DebounceTTLSeconds: debounceTTL,
		LeaderElectionNamespace:      leaderElectionNamespace,
		LeaderElectionID:             leaderElectionID,
		LeaderElectionResourceLock:   leaderElectionResourceLock,
		LeaderElectionLeaseDuration:  leaseDuration,
		LeaderElectionRenewDeadline:  renewDeadline,
		LeaderElectionRetryPeriod:    retryPeriod,
	}, nil
}
