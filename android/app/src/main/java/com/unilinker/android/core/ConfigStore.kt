package com.unilinker.android.core

import android.content.Context
import com.unilinker.android.sdk.IConfigStore
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.asSharedFlow

class ConfigStore(context: Context) : IConfigStore {

    private val prefs = context.getSharedPreferences("unilinker", Context.MODE_PRIVATE)
    private val _changes = MutableSharedFlow<String>(extraBufferCapacity = 10)

    override fun <T> get(key: String, default: T): T {
        @Suppress("UNCHECKED_CAST")
        return when (default) {
            is String -> prefs.getString(key, default) as T
            is Int -> prefs.getInt(key, default) as T
            is Boolean -> prefs.getBoolean(key, default) as T
            is Float -> prefs.getFloat(key, default) as T
            is Long -> prefs.getLong(key, default) as T
            else -> default
        }
    }

    override fun <T> set(key: String, value: T) {
        val editor = prefs.edit()
        when (value) {
            is String -> editor.putString(key, value)
            is Int -> editor.putInt(key, value)
            is Boolean -> editor.putBoolean(key, value)
            is Float -> editor.putFloat(key, value)
            is Long -> editor.putLong(key, value)
            else -> return
        }
        editor.apply()
        _changes.tryEmit(key)
    }

    override fun observe(key: String): Flow<String?> {
        return _changes.asSharedFlow().let { flow ->
            kotlinx.coroutines.flow.flow {
                emit(get(key, null as String?))
                flow.collect { changedKey ->
                    if (changedKey == key) emit(get(key, null as String?))
                }
            }
        }
    }

    override suspend fun save() {
        // SharedPreferences auto-saves on apply()
    }
}
