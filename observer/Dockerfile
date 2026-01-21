# Stage 1: Build
FROM golang:1.22-alpine AS builder

WORKDIR /app

# Copy go.mod and go.sum files to download dependencies
COPY go.mod go.sum ./
RUN go mod download

# Copy the rest of the source code
COPY . .

# Build the application
# CGO_ENABLED=0 is important for a truly static binary
# -o /app/observer builds the output binary in the desired location
RUN CGO_ENABLED=0 go build -o /app/observer ./cmd/main.go

# Stage 2: Final image
# Use a distroless image for a smaller and more secure final image
FROM gcr.io/distroless/static-debian11

WORKDIR /

# Copy the binary from the builder stage
COPY --from=builder /app/observer /observer

# Set the user to non-root for security
USER nonroot:nonroot

# The command to run the application
ENTRYPOINT ["/observer"]
