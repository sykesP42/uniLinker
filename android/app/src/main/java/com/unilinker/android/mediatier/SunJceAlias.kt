package com.unilinker.android.mediatier

import java.security.Provider
import java.security.Security

/**
 * jitsi-srtp on Android compat: its default cipher factory resolves the JCE
 * provider by the literal name "SunJCE" (an OpenJDK name that does not exist
 * on Android), which throws NullPointerException in its static initializer.
 *
 * The fix is a tiny alias Provider NAMED "SunJCE" whose cipher services
 * forward to the platform provider (Conscrypt), which implements all the
 * transformations jitsi-srtp requests: AES/CTR/NoPadding (RFC 3711 CTR),
 * AES/GCM/NoPadding and AES/ECB/NoPadding.
 *
 * Register once per process, before any jitsi-srtp class is initialized.
 */
object SunJceAlias {
    private val transformations = listOf(
        "AES/CTR/NoPadding",
        "AES/GCM/NoPadding",
        "AES/ECB/NoPadding",
    )

    @Volatile
    private var registered = false

    fun register() {
        if (registered) return
        synchronized(SunJceAlias::class.java) {
            if (registered) return
            if (Security.getProvider("SunJCE") != null) {
                registered = true
                return
            }

            // Find a platform provider that implements the AES transformations.
            val platform = Security.getProviders().firstOrNull { p ->
                transformations.any { algo -> hasCipher(p, algo) }
            } ?: return
            Security.insertProviderAt(SunJceAliasProvider(platform, transformations), 1)
            registered = true
        }
    }

    private fun hasCipher(provider: Provider, algo: String) =
        provider.services.any {
            it.type == "Cipher" && it.algorithm.equals(algo, ignoreCase = true)
        }
}

/**
 * Alias provider named "SunJCE": every registered cipher service delegates
 * to the real platform provider's service. Pure plumbing.
 */
private class SunJceAliasProvider(
    private val platform: Provider,
    transformations: List<String>,
) : Provider("SunJCE", 1.0, "Alias forwarding AES cipher requests to the platform provider (jitsi-srtp Android compat)") {
    init {
        for (algo in transformations) {
            val real = platform.services.firstOrNull {
                it.type == "Cipher" && it.algorithm.equals(algo, ignoreCase = true)
            } ?: continue
            putService(object : Provider.Service(this, "Cipher", algo, real.className, null, null) {
                override fun newInstance(constructorParameter: Any?): Any =
                    real.newInstance(constructorParameter)
            })
        }
    }
}
