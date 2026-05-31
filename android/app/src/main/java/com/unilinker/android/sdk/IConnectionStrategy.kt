package com.unilinker.android.sdk

import com.unilinker.android.sdk.models.PeerDevice
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.SharedFlow

/**
 * 连接策略：定义如何发现设备、建立连接、接受入站连接。
 * 每种连接方式（局域网、手动IP、连接码等）都是一个策略实例。
 */
interface IConnectionStrategy {

    /** 策略标识 */
    val id: String

    /** 显示名称 */
    val name: String

    /** 图标 */
    val icon: String

    /** 适用场景描述 */
    val description: String

    /** 是否自动发现（无需用户交互） */
    val autoDiscover: Boolean

    /** 是否需要信令服务器 */
    val needsRelay: Boolean

    /**
     * 启动策略（开始发现/监听）
     */
    suspend fun start()

    /**
     * 停止策略
     */
    suspend fun stop()

    /**
     * 发现的设备列表（持续更新）
     */
    fun discover(): Flow<List<PeerDevice>>

    /**
     * 连接到指定设备，返回建立好的 Peer 连接
     */
    suspend fun connect(peer: PeerDevice): Result<IPeerMesh>

    /**
     * 入站连接请求（当有人连接本机时触发）
     */
    val incomingConnections: SharedFlow<ConnectionRequest>

    /**
     * 接受入站连接
     */
    suspend fun accept(request: ConnectionRequest): Result<IPeerMesh>

    /**
     * 拒绝入站连接
     */
    suspend fun reject(request: ConnectionRequest)

    /**
     * 获取本机连接信息（用于分享给他人，如 IP:Port 或 连接码）
     */
    fun getShareInfo(): ShareInfo
}

data class ConnectionRequest(
    val id: String,
    val fromDevice: PeerDevice,
    val strategyId: String,
    val timestamp: Long = System.currentTimeMillis(),
)

data class ShareInfo(
    val type: ShareType,
    val value: String,         // 如 "192.168.1.5:9527" 或 "ABC123"
    val displayText: String,   // 给用户看的友好文本
)

enum class ShareType {
    IP_PORT,       // 手动输入
    CONNECTION_CODE, // 6位连接码
    QR_CODE_URL,   // 扫码连接
}
