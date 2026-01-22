package comms_test

import (
	"context"
	"net"
	"testing"

	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
	"google.golang.org/grpc"
	"google.golang.org/grpc/codes"
	"google.golang.org/grpc/status"
	"google.golang.org/grpc/test/bufconn"

	"kube-mind/observer/internal/comms"
	pb "kube-mind/observer/proto"
)

// mockIncidentService is a mock implementation of the pb.IncidentServiceServer.
type mockIncidentService struct {
	pb.UnimplementedIncidentServiceServer
	lastIncident *pb.IncidentContext
	streamErr    error // To simulate an error during streaming
}

func (s *mockIncidentService) StreamIncident(stream pb.IncidentService_StreamIncidentServer) error {
	if s.streamErr != nil {
		return s.streamErr
	}

	// In a client-streaming RPC, the server receives multiple messages from the client.
	// For this test, we only expect one incident context to be streamed.
	inc, err := stream.Recv()
	if err != nil {
		return err
	}
	s.lastIncident = inc

	return stream.SendAndClose(&pb.StreamIncidentResponse{Status: "Received"})
}

func TestBrainGrpcClient_StreamIncident(t *testing.T) {
	ctx := context.Background()
	listener := bufconn.Listen(1024 * 1024)
	s := grpc.NewServer()
	mockService := &mockIncidentService{}
	pb.RegisterIncidentServiceServer(s, mockService)

	go func() {
		if err := s.Serve(listener); err != nil {
			t.Logf("Server exited with error: %v", err)
		}
	}()
	defer s.Stop()

	// Create a client connection to the buffconn listener
	dialer := func(context.Context, string) (net.Conn, error) {
		return listener.Dial()
	}
	conn, err := grpc.DialContext(ctx, "bufnet", grpc.WithContextDialer(dialer), grpc.WithInsecure())
	require.NoError(t, err)
	defer conn.Close()

	// Create the gRPC client instance
	client := &comms.BrainGrpcClient{
		Conn:   conn,
		Client: pb.NewIncidentServiceClient(conn),
	}

	incident := &pb.IncidentContext{
		IncidentId: "test-incident",
		PodName:    "test-pod",
	}

	// Test successful streaming
	err = client.StreamIncident(ctx, incident)
	require.NoError(t, err)
	assert.NotNil(t, mockService.lastIncident)
	assert.Equal(t, incident.IncidentId, mockService.lastIncident.IncidentId)

	// Test streaming error
	mockService.streamErr = status.Error(codes.Internal, "internal server error")
	err = client.StreamIncident(ctx, incident)
	require.Error(t, err)
	st, ok := status.FromError(err)
	assert.True(t, ok)
	assert.Contains(t, st.Message(), "internal server error")
	assert.Equal(t, codes.Internal, st.Code())
}
