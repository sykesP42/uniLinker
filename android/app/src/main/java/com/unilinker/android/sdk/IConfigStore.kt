package com.unilinker.android.sdk

import kotlinx.coroutines.flow.Flow

interface IConfigStore {
    fun <T> get(key: String, default: T): T
    fun <T> set(key: String, value: T)
    fun observe(key: String): Flow<String?>
    suspend fun save()
}
