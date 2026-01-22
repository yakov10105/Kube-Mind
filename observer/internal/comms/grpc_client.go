package comms

import (
	"context"
	"crypto/tls"
	"crypto/x509"
	"fmt"
	"os"
	"time"

	"google.golang.org/grpc"
	"google.golang.org/grpc/credentials"
	"google.golang.org/grpc/keepalive"

	pb "kube-mind/observer/proto"
)

// GrpcClient defines the interface for a gRPC client.
type GrpcClient interface {
	StreamIncident(ctx context.Context, incident *pb.IncidentContext) error
	Close() error
}

// BrainGrpcClient implements GrpcClient to communicate with the .NET Brain service.
type BrainGrpcClient struct {
	Conn   *grpc.ClientConn
	Client pb.IncidentServiceClient
}

// NewBrainGrpcClient creates and connects a new BrainGrpcClient.
func NewBrainGrpcClient(ctx context.Context, addr, caCertPath, clientCertPath, clientKeyPath string) (*BrainGrpcClient, error) {
	// Load the client's certificate and private key
	clientCert, err := tls.LoadX509KeyPair(clientCertPath, clientKeyPath)
	if err != nil {
		return nil, fmt.Errorf("failed to load client key pair: %w", err)
	}

	// Load the CA certificate
	caCert, err := os.ReadFile(caCertPath)
	if err != nil {
		return nil, fmt.Errorf("failed to read CA certificate: %w", err)
	}
	caCertPool := x509.NewCertPool()
	if !caCertPool.AppendCertsFromPEM(caCert) {
		return nil, fmt.Errorf("failed to add CA certificate to pool")
	}

	// Create TLS credentials
	tlsConfig := &tls.Config{
		Certificates: []tls.Certificate{clientCert},
		RootCAs:      caCertPool,
	}
	creds := credentials.NewTLS(tlsConfig)

	// Set up gRPC dial options
	opts := []grpc.DialOption{
		grpc.WithTransportCredentials(creds),
		grpc.WithBlock(), // Block until the connection is established
		grpc.WithKeepaliveParams(keepalive.ClientParameters{
			Time:                10 * time.Second,
			Timeout:             time.Second,
			PermitWithoutStream: true,
		}),
	}

	// Dial the server
	conn, err := grpc.DialContext(ctx, addr, opts...)
	if err != nil {
		return nil, fmt.Errorf("failed to dial gRPC server: %w", err)
	}

	return &BrainGrpcClient{
		Conn:   conn,
		Client: pb.NewIncidentServiceClient(conn),
	}, nil
}

// StreamIncident sends an incident to the Brain service.
func (c *BrainGrpcClient) StreamIncident(ctx context.Context, incident *pb.IncidentContext) error {
	stream, err := c.Client.StreamIncident(ctx)
	if err != nil {
		return fmt.Errorf("failed to create incident stream: %w", err)
	}
	if err := stream.Send(incident); err != nil {
		return fmt.Errorf("failed to send incident on stream: %w", err)
	}
	// Close the stream and receive the server's response
	if _, err := stream.CloseAndRecv(); err != nil {
		return fmt.Errorf("failed to close and receive from stream: %w", err)
	}
	return nil
}

// Close closes the gRPC connection.
func (c *BrainGrpcClient) Close() error {
	if c.Conn != nil {
		return c.Conn.Close()
	}
	return nil
}
