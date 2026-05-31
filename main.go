package main

import (
	"flag"
	"fmt"
	"log"
	"net"
	"net/http"
	"os"
	"os/signal"
	"syscall"
)

func main() {
	port := flag.Int("port", 8080, "HTTP server port")
	flag.Parse()

	fmt.Println("========================================")
	fmt.Println("  UniLinker - Screen Mirroring")
	fmt.Println("  VP8 + WebRTC (optimized)")
	fmt.Println("========================================")
	fmt.Println()

	log.Println("Initializing screen capture...")
	stream, err := NewScreenStream()
	if err != nil {
		log.Fatalf("Capture init failed: %v", err)
	}
	defer func() {
		for _, t := range stream.GetTracks() {
			t.Close()
		}
	}()
	log.Println("Screen capture ready")

	server := NewServer(stream)

	mux := http.NewServeMux()
	mux.HandleFunc("/", server.handleIndex)
	mux.HandleFunc("/offer", server.handleOffer)
	mux.HandleFunc("/status", server.handleStatus)

	addr := fmt.Sprintf(":%d", *port)
	httpServer := &http.Server{Addr: addr, Handler: mux}
	go func() {
		if err := httpServer.ListenAndServe(); err != http.ErrServerClosed {
			log.Fatalf("HTTP error: %v", err)
		}
	}()

	localIP := getLocalIP()
	fmt.Printf("Server started!\n")
	fmt.Printf("  Local:   http://localhost:%d\n", *port)
	if localIP != "" {
		fmt.Printf("  Network: http://%s:%d\n", localIP, *port)
	}
	fmt.Printf("\nOpen the URL in a browser to view the screen.\n")
	fmt.Printf("Press Ctrl+C to stop.\n\n")

	sigCh := make(chan os.Signal, 1)
	signal.Notify(sigCh, syscall.SIGINT, syscall.SIGTERM)
	<-sigCh
	fmt.Println("\nShutting down...")
}

func getLocalIP() string {
	ifaces, _ := net.Interfaces()
	for _, iface := range ifaces {
		if iface.Flags&net.FlagUp == 0 || iface.Flags&net.FlagLoopback != 0 {
			continue
		}
		addrs, _ := iface.Addrs()
		for _, addr := range addrs {
			var ip net.IP
			switch v := addr.(type) {
			case *net.IPNet:
				ip = v.IP
			case *net.IPAddr:
				ip = v.IP
			}
			if ip != nil && !ip.IsLoopback() {
				if ip4 := ip.To4(); ip4 != nil {
					return ip4.String()
				}
			}
		}
	}
	return ""
}
