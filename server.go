package main

import (
	"encoding/json"
	"fmt"
	"log"
	"net/http"
	"sync"

	"github.com/pion/mediadevices"
	"github.com/pion/webrtc/v4"
)

// Server manages WebRTC connections and screen streaming.
type Server struct {
	stream  mediadevices.MediaStream
	mu      sync.Mutex
	peers   map[string]*webrtc.PeerConnection
	stunURL string
}

// NewServer creates a streaming server.
func NewServer(stream mediadevices.MediaStream) *Server {
	return &Server{
		stream:  stream,
		peers:   make(map[string]*webrtc.PeerConnection),
		stunURL: "stun:stun.l.google.com:19302",
	}
}

// handleOffer processes WebRTC offer and returns SDP answer.
func (s *Server) handleOffer(w http.ResponseWriter, r *http.Request) {
	if r.Method != http.MethodPost {
		http.Error(w, "Method not allowed", http.StatusMethodNotAllowed)
		return
	}

	var offer webrtc.SessionDescription
	if err := json.NewDecoder(r.Body).Decode(&offer); err != nil {
		http.Error(w, "Invalid JSON: "+err.Error(), http.StatusBadRequest)
		return
	}

	config := webrtc.Configuration{
		ICEServers: []webrtc.ICEServer{
			{URLs: []string{s.stunURL}},
		},
	}

	pc, err := webrtc.NewPeerConnection(config)
	if err != nil {
		http.Error(w, "PeerConnection: "+err.Error(), http.StatusInternalServerError)
		return
	}

	for _, track := range s.stream.GetTracks() {
		track.OnEnded(func(err error) { log.Printf("Track ended: %v", err) })
		if _, err := pc.AddTrack(track.(webrtc.TrackLocal)); err != nil {
			http.Error(w, "AddTrack: "+err.Error(), http.StatusInternalServerError)
			_ = pc.Close()
			return
		}
	}

	peerID := fmt.Sprintf("%p", pc)
	pc.OnConnectionStateChange(func(state webrtc.PeerConnectionState) {
		log.Printf("[%s] %s", peerID[:8], state.String())
		if state == webrtc.PeerConnectionStateDisconnected ||
			state == webrtc.PeerConnectionStateFailed ||
			state == webrtc.PeerConnectionStateClosed {
			s.mu.Lock()
			delete(s.peers, peerID)
			s.mu.Unlock()
			_ = pc.Close()
		}
	})

	s.mu.Lock()
	s.peers[peerID] = pc
	s.mu.Unlock()

	if err := pc.SetRemoteDescription(offer); err != nil {
		http.Error(w, "SetRemote: "+err.Error(), http.StatusInternalServerError)
		_ = pc.Close()
		return
	}

	answer, err := pc.CreateAnswer(nil)
	if err != nil {
		http.Error(w, "CreateAnswer: "+err.Error(), http.StatusInternalServerError)
		_ = pc.Close()
		return
	}

	if err := pc.SetLocalDescription(answer); err != nil {
		http.Error(w, "SetLocal: "+err.Error(), http.StatusInternalServerError)
		_ = pc.Close()
		return
	}

	<-webrtc.GatheringCompletePromise(pc)

	w.Header().Set("Content-Type", "application/json")
	_ = json.NewEncoder(w).Encode(pc.LocalDescription())
}

func (s *Server) peerCount() int {
	s.mu.Lock()
	defer s.mu.Unlock()
	return len(s.peers)
}

// handleStatus returns server status.
func (s *Server) handleStatus(w http.ResponseWriter, r *http.Request) {
	w.Header().Set("Content-Type", "application/json")
	_ = json.NewEncoder(w).Encode(map[string]interface{}{
		"status":  "running",
		"peers":   s.peerCount(),
		"stun":    s.stunURL,
		"version": "2.0-opt",
	})
}

// handleIndex serves the frontend.
func (s *Server) handleIndex(w http.ResponseWriter, r *http.Request) {
	if r.URL.Path != "/" {
		http.ServeFile(w, r, "static"+r.URL.Path)
		return
	}
	http.ServeFile(w, r, "static/index.html")
}
