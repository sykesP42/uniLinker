# UniLinker

统一远程连接工具 — 低延迟、高质量的远程桌面投屏。

## 当前版本 (v2.0)

**技术栈**：Go + pion/mediadevices (VP8) + pion/webrtc v4

- ✅ 实时屏幕投屏（WebRTC + VP8 编码）
- ✅ 浏览器端观看（Chrome/Edge/Firefox）
- ✅ 实时状态（分辨率、帧率、延迟）
- ✅ 全屏模式

## 运行

```bash
./unilinker.exe
# 浏览器打开 http://10.200.78.168:8080 → Connect
```

## 架构

```
┌──────────────────┐      WebRTC (VP8)      ┌──────────────────┐
│  被控端 (Go)      │ ◄═════════════════════► │  浏览器           │
│ mediadevices     │                         │ <video> 渲染      │
│  └ VP8 编码       │                         │ 实时状态面板       │
│ pion/webrtc v4   │                         │                  │
└──────────────────┘                         └──────────────────┘
```

## 项目结构

```
uniLinker/
├── main.go                  # 入口
├── capture.go               # 屏幕捕获 (mediadevices + VP8)
├── server.go                # WebRTC 信令
├── static/
│   ├── index.html           # 前端
│   └── client.js            # WebRTC 客户端
├── native/
│   ├── dxgi_capture.h/c     # DXGI 捕获模块（备用）
│   └── dxgi_capture.dll     # 预编译 DXGI DLL（仅依赖系统 D3D11）
├── go.mod
└── README.md
```

## 升级路径

| 升级项 | 收益 | 状态 |
|--------|------|------|
| DXGI 捕获 | 延迟 10-30ms → 1-2ms | C 模块已就绪 (`native/dxgi_capture.dll`) |
| H.264 硬件编码 | 更好的质量和兼容性 | 需要自编译 FFmpeg（最小依赖版） |
| WebTransport | 更低传输延迟 | 待实现 |
