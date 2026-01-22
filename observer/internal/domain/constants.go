package domain

// K8s constants for pod states and reasons.
const (
	// ReasonCrashLoopBackOff is the reason for a container that is restarting in a loop.
	ReasonCrashLoopBackOff = "CrashLoopBackOff"
)

// Harvester constants for data gathering parameters.
const (
	// DefaultLogTailLines is the default number of log lines to fetch.
	DefaultLogTailLines = 200
)
