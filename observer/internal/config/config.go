package config

import (
	"os"
	"time"
)

// ControllerConfig holds the configuration for the observer controller.
type ControllerConfig struct {
	LogLevel                    string
	DebounceTTLSeconds          time.Duration
	LeaderElectionNamespace     string
	LeaderElectionID            string
	LeaderElectionResourceLock  string
	LeaderElectionLeaseDuration time.Duration
	LeaderElectionRenewDeadline time.Duration
	LeaderElectionRetryPeriod   time.Duration
}

const (
	defaultLogLevel                    = "info"
	defaultDebounceTTL                 = 300 * time.Second
	defaultLeaderElectionNamespace     = "default"
	defaultLeaderElectionID            = "19767522.tutorial.kubebuilder.io"
	defaultLeaderElectionResourceLock  = "leases"
	defaultLeaderElectionLeaseDuration = 15 * time.Second
	defaultLeaderElectionRenewDeadline = 10 * time.Second
	defaultLeaderElectionRetryPeriod   = 2 * time.Second
)

// LoadConfig loads configuration from environment variables.
func LoadConfig() (*ControllerConfig, error) {
	logLevel := os.Getenv("LOG_LEVEL")
	if logLevel == "" {
		logLevel = defaultLogLevel
	}

	debounceTTLStr := os.Getenv("DEBOUNCE_TTL_SECONDS")
	debounceTTL, err := time.ParseDuration(debounceTTLStr + "s")
	if err != nil || debounceTTL <= 0 {
		debounceTTL = defaultDebounceTTL
	}

	leaderElectionNamespace := os.Getenv("LEADER_ELECTION_NAMESPACE")
	if leaderElectionNamespace == "" {
		leaderElectionNamespace = defaultLeaderElectionNamespace
	}

	leaderElectionID := os.Getenv("LEADER_ELECTION_ID")
	if leaderElectionID == "" {
		leaderElectionID = defaultLeaderElectionID
	}

	leaderElectionResourceLock := os.Getenv("LEADER_ELECTION_RESOURCE_LOCK")
	if leaderElectionResourceLock == "" {
		leaderElectionResourceLock = defaultLeaderElectionResourceLock
	}

	leaseDurationStr := os.Getenv("LEADER_ELECTION_LEASE_DURATION")
	leaseDuration, err := time.ParseDuration(leaseDurationStr)
	if err != nil || leaseDuration <= 0 {
		leaseDuration = defaultLeaderElectionLeaseDuration
	}

	renewDeadlineStr := os.Getenv("LEADER_ELECTION_RENEW_DEADLINE")
	renewDeadline, err := time.ParseDuration(renewDeadlineStr)
	if err != nil || renewDeadline <= 0 {
		renewDeadline = defaultLeaderElectionRenewDeadline
	}

	retryPeriodStr := os.Getenv("LEADER_ELECTION_RETRY_PERIOD")
	retryPeriod, err := time.ParseDuration(retryPeriodStr)
	if err != nil || retryPeriod <= 0 {
		retryPeriod = defaultLeaderElectionRetryPeriod
	}

	return &ControllerConfig{
		LogLevel:                    logLevel,
		DebounceTTLSeconds:          debounceTTL,
		LeaderElectionNamespace:     leaderElectionNamespace,
		LeaderElectionID:            leaderElectionID,
		LeaderElectionResourceLock:  leaderElectionResourceLock,
		LeaderElectionLeaseDuration: leaseDuration,
		LeaderElectionRenewDeadline: renewDeadline,
		LeaderElectionRetryPeriod:   retryPeriod,
	}, nil
}
