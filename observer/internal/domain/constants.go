package domain

// K8s constants for pod states and reasons.
const (
	// ReasonCrashLoopBackOff is the reason for a container that is restarting in a loop.
	ReasonCrashLoopBackOff = "CrashLoopBackOff"
	// ReasonImagePullBackOff is the reason for a container that cannot pull its image.
	ReasonImagePullBackOff = "ImagePullBackOff"
	// ReasonErrImagePull is the reason for a container that encountered an error during image pull.
	ReasonErrImagePull = "ErrImagePull"
	// ReasonOOMKilled is the reason for a container that was terminated due to out of memory.
	ReasonOOMKilled = "OOMKilled"
	// ReasonError is a generic error reason for a terminated container.
	ReasonError = "Error"
)

// Harvester constants for data gathering parameters.
const (
	// DefaultLogTailLines is the default number of log lines to fetch.
	DefaultLogTailLines = 200
)
