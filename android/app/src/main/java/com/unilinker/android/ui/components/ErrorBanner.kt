package com.unilinker.android.ui.components

import androidx.compose.animation.*
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/**
 * 错误严重程度
 */
enum class ErrorSeverity {
    Info,       // 蓝色，3秒自动消失
    Warning,    // 黄色，需手动关闭
    Error,      // 红色，提供解决方案
    Critical    // 全屏模态对话框，阻止操作
}

/**
 * 错误信息数据类
 */
data class ErrorInfo(
    val severity: ErrorSeverity,
    val message: String,
    val suggestions: List<String> = emptyList(),
    val actionLabel: String? = null,
    val onAction: (() -> Unit)? = null,
    val isDismissible: Boolean = true,
    val errorCode: String? = null
)

/**
 * 错误横幅组件
 *
 * @param error 错误信息
 * @param onDismiss 关闭回调
 * @param modifier 修饰符
 */
@Composable
fun ErrorBanner(
    error: ErrorInfo?,
    onDismiss: () -> Unit,
    modifier: Modifier = Modifier
) {
    AnimatedVisibility(
        visible = error != null,
        enter = slideInVertically() + fadeIn(),
        exit = slideOutVertically() + fadeOut(),
        modifier = modifier
    ) {
        error?.let { err ->
            val (bgColor, borderColor, icon, iconColor) = when (err.severity) {
                ErrorSeverity.Info -> Tuple4(
                    Color(0xFF1A3B82F6),
                    Color(0xFF3B82F6),
                    Icons.Default.Info,
                    Color(0xFF3B82F6)
                )
                ErrorSeverity.Warning -> Tuple4(
                    Color(0xFF1AF59E0B),
                    Color(0xFFF59E0B),
                    Icons.Default.Warning,
                    Color(0xFFF59E0B)
                )
                ErrorSeverity.Error -> Tuple4(
                    Color(0xFF1AEF4444),
                    Color(0xFFEF4444),
                    Icons.Default.Error,
                    Color(0xFFEF4444)
                )
                ErrorSeverity.Critical -> Tuple4(
                    Color(0xFF1AEF4444),
                    Color(0xFFEF4444),
                    Icons.Default.Dangerous,
                    Color(0xFFEF4444)
                )
            }

            Card(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 16.dp, vertical = 8.dp),
                colors = CardDefaults.cardColors(containerColor = bgColor),
                shape = RoundedCornerShape(12.dp),
                border = androidx.compose.foundation.BorderStroke(1.dp, borderColor)
            ) {
                Column(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(16.dp)
                ) {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Box(
                            modifier = Modifier
                                .size(24.dp)
                                .clip(RoundedCornerShape(12.dp))
                                .background(borderColor.copy(alpha = 0.2f)),
                            contentAlignment = Alignment.Center
                        ) {
                            Icon(
                                icon,
                                contentDescription = null,
                                tint = iconColor,
                                modifier = Modifier.size(14.dp)
                            )
                        }

                        Spacer(modifier = Modifier.width(12.dp))

                        Text(
                            err.message,
                            modifier = Modifier.weight(1f),
                            color = Color(0xFFE5E5E5),
                            fontWeight = FontWeight.Medium
                        )

                        if (err.isDismissible) {
                            IconButton(
                                onClick = onDismiss,
                                modifier = Modifier.size(24.dp)
                            ) {
                                Icon(
                                    Icons.Default.Close,
                                    contentDescription = "关闭",
                                    tint = Color(0xFF737373),
                                    modifier = Modifier.size(16.dp)
                                )
                            }
                        }
                    }

                    // 显示建议
                    if (err.suggestions.isNotEmpty()) {
                        Column(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(top = 8.dp, start = 36.dp)
                        ) {
                            err.suggestions.forEach { suggestion ->
                                Text(
                                    "• $suggestion",
                                    fontSize = 12.sp,
                                    color = Color(0xFFA3A3A3)
                                )
                            }
                        }
                    }

                    // 操作按钮
                    if (err.actionLabel != null && err.onAction != null) {
                        Spacer(modifier = Modifier.height(8.dp))
                        TextButton(
                            onClick = err.onAction,
                            modifier = Modifier.padding(start = 36.dp)
                        ) {
                            Text(
                                err.actionLabel,
                                color = borderColor,
                                fontWeight = FontWeight.Medium
                            )
                        }
                    }
                }
            }
        }
    }
}

// 辅助数据类，因为 Kotlin 没有 Tuple4
private data class Tuple4<A, B, C, D>(
    val first: A,
    val second: B,
    val third: C,
    val fourth: D
)
