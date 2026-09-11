plugins {
    // AGP 9 compiles Kotlin itself (built-in Kotlin); applying org.jetbrains.kotlin.android is now an error.
    id("com.android.application") version "9.4.0" apply false
    // 14.1.0 is the first release that supports AGP's built-in Kotlin.
    id("org.jlleitschuh.gradle.ktlint") version "14.2.0" apply false
}
