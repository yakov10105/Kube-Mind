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
	inc, err := stream.Recv()
	if err != nil {
		return err
	}
	s.lastIncident = inc
	return stream.SendAndClose(&pb.StreamIncidentResponse{Status: "Received"})
}

func TestBrainGrpcClient_StreamIncident(t *testing.T) {
	t.Parallel()
	ctx := context.Background()

	incident := &pb.IncidentContext{
		IncidentId: "test-incident",
		PodName:    "test-pod",
	}

	testCases := []struct {
		name                string
		mockService         *mockIncidentService
		expectErr           bool
		expectedErrCode     codes.Code
		expectedErrContains string
	}{
		{
			name:        "Successful stream",
			mockService: &mockIncidentService{},
			expectErr:   false,
		},
		{
			name: "Stream error from server",
			mockService: &mockIncidentService{
				streamErr: status.Error(codes.Internal, "internal server error"),
			},
			expectErr:           true,
			expectedErrCode:     codes.Internal,
			expectedErrContains: "internal server error",
		},
	}

	for _, tc := range testCases {
		tc := tc
		t.Run(tc.name, func(t *testing.T) {
			t.Parallel()

			listener := bufconn.Listen(1024 * 1024)
			s := grpc.NewServer()
			pb.RegisterIncidentServiceServer(s, tc.mockService)

			go func() {
				_ = s.Serve(listener)
			}()
			defer s.Stop()

			dialer := func(context.Context, string) (net.Conn, error) {
				return listener.Dial()
			}
			conn, err := grpc.DialContext(ctx, "bufnet", grpc.WithContextDialer(dialer), grpc.WithInsecure())
			require.NoError(t, err)
			defer conn.Close()

			client := &comms.BrainGrpcClient{
				Conn:   conn,
				Client: pb.NewIncidentServiceClient(conn),
			}

			err = client.StreamIncident(ctx, incident)

			if tc.expectErr {
				require.Error(t, err)
				st, ok := status.FromError(err)
				require.True(t, ok)
				assert.Equal(t, tc.expectedErrCode, st.Code())
				assert.Contains(t, st.Message(), tc.expectedErrContains)
			} else {
				require.NoError(t, err)
				assert.NotNil(t, tc.mockService.lastIncident)
				assert.Equal(t, incident.IncidentId, tc.mockService.lastIncident.IncidentId)
			}
		})
	}
}
