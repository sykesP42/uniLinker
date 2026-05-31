package main

import (
	"fmt"

	"github.com/pion/mediadevices"
	"github.com/pion/mediadevices/pkg/codec/vpx"
	"github.com/pion/mediadevices/pkg/prop"
	_ "github.com/pion/mediadevices/pkg/driver/screen"
)

// NewScreenStream creates an optimized VP8 screen capture stream.
func NewScreenStream() (mediadevices.MediaStream, error) {
	vp8Params, err := vpx.NewVP8Params()
	if err != nil {
		return nil, fmt.Errorf("VP8 params: %w", err)
	}

	codecSelector := mediadevices.NewCodecSelector(
		mediadevices.WithVideoEncoders(&vp8Params),
	)

	stream, err := mediadevices.GetDisplayMedia(mediadevices.MediaStreamConstraints{
		Video: func(c *mediadevices.MediaTrackConstraints) {
			c.Width = prop.Int(1920)
			c.Height = prop.Int(1080)
			c.FrameRate = prop.Float(30)
		},
		Codec: codecSelector,
	})
	if err != nil {
		return nil, fmt.Errorf("display media: %w", err)
	}
	return stream, nil
}
