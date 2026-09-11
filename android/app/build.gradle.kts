plugins {
    id("com.android.application")
    id("org.jlleitschuh.gradle.ktlint")
}

android {
    namespace = "me.akshitbansal.edgepad"
    compileSdk = 37

    defaultConfig {
        applicationId = "me.akshitbansal.edgepad"
        // Android 12: one Bluetooth permission path (BLUETOOTH_CONNECT).
        minSdk = 31
        targetSdk = 37
        versionCode = 1
        versionName = "0.1.0"
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    lint {
        warningsAsErrors = true
        abortOnError = true
        // Version-freshness checks turn a green build red on the day a new release ships, with no change
        // to this repo. Upgrading is a deliberate step, not a lint failure. Every other check stays on.
        disable += setOf("GradleDependency", "AndroidGradlePluginVersion", "NewerVersionAvailable", "OldTargetApi")
    }
}

kotlin {
    compilerOptions {
        allWarningsAsErrors = true
    }
}

// Pinned so CI and a local ktlint CLI run the same rule set.
ktlint {
    version.set("1.8.0")
}

dependencies {
    testImplementation("junit:junit:4.13.2")
}
