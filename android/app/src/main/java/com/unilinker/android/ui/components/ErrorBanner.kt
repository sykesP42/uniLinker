package com.unilinker.android.ui.components

import androidx.compose.animation.*
import androidx.compose.animation.core.tween
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.unilinker.android.ui.theme.*

/**
 * 错误严重程度
 */
enum class ErrorSeverity {
    Info,       // 铜金色，3秒自动消失
    Warning,    // 琥珀金，需手动关闭
    Error,      // 暖红，提供解决方案
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
 * 工业风错误横幅组件
 *
 * - 无圆角
 * - 无边框（直接背景）
 * - 线性图标
 */
@Composable
fun ErrorBanner(
    error: ErrorInfo?,
    onDismiss: () -> Unit,
    modifier: Modifier = Modifier
) {
    AnimatedVisibility(
        visible = error != null,
        enter = slideInVertically(
            animationSpec = tween(IndustrialDesign.ANIM_FAST)
        ) + fadeIn(tween(IndustrialDesign.ANIM_FAST)),
        exit = slideOutVertically(
            animationSpec = tween(IndustrialDesign.ANIM_FAST)
        ) + fadeOut(tween(IndustrialDesign.ANIM_FAST)),
        modifier = modifier
    ) {
        error?.let { err ->
            val (bgColor, iconColor) = when (err.severity) {
                ErrorSeverity.Info -> Pair(BackgroundVariant, Copper)
                ErrorSeverity.Warning -> Pair(StatusWarning.copy(alpha = 0.15f), StatusWarning)
                ErrorSeverity.Error -> Pair(StatusError, StatusError)
                ErrorSeverity.Critical -> Pair(StatusError, StatusError)
            }

            Column(
                modifier = Modifier
                    .fillMaxWidth()
                    .background(bgColor)
            ) {
                Row(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(horizontal = 16.dp, vertical = 12.dp),
                    verticalAlignment = Alignment.CenterVertically
                ) {
                    Icon(
                        Icons.Outlined.Warning,
                        contentDescription = null,
                        tint = iconColor,
                        modifier = Modifier.size(20.dp)
                    )

                    Spacer(modifier = Modifier.width(12.dp))

                    Text(
                        err.message,
                        modifier = Modifier.weight(1f),
                        color = if (err.severity == ErrorSeverity.Error || err.severity == ErrorSeverity.Critical) {
                            TextPrimary
                        } else {
                            TextSecondary
                        },
                        fontWeight = FontWeight.Medium,
                        fontSize = 13.sp
                    )

                    if (err.isDismissible) {
                        IconButton(
                            onClick = onDismiss,
                            modifier = Modifier.size(24.dp)
                        ) {
                            Icon(
                                Icons.Outlined.Close,
                                contentDescription = "关闭",
                                tint = TextTertiary,
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
                            .padding(start = 48.dp, end = 16.dp, bottom = 12.dp)
                    ) {
                        err.suggestions.forEach { suggestion ->
                            Text(
                                "• $suggestion",
                                fontSize = 12.sp,
                                color = TextTertiary
                            )
                        }
                    }
                }

                // 操作按钮
                if (err.actionLabel != null && err.onAction != null) {
                    TextButton(
                        onClick = err.onAction,
                        modifier = Modifier.padding(start = 48.dp, bottom = 12.dp),
                        colors = ButtonDefaults.textButtonColors(
                            contentColor = iconColor
                        )
                    ) {
                        Text(
                            err.actionLabel,
                            fontWeight = FontWeight.Bold,
                            fontSize = 12.sp
                        )
                    }
                }
            }
        }
    }
}