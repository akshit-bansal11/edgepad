plugins {
    id("com.android.application")
    id("org.jlleitschuh.gradle.ktlint")
}

// Release signing comes from the environment, which only .github/workflows/release.yml sets.
// The key never enters the repository; without it a release build is simply unsigned.
val releaseKeystore = providers.environmentVariable("EDGEPAD_KEYSTORE_PATH").orNull

android {
    namespace = "me.akshitbansal.edgepad"
    compileSdk = 37

    defaultConfig {
        applicationId = "me.akshitbansal.edgepad"
        // Android 12: one Bluetooth permission path (BLUETOOTH_CONNECT).
        minSdk = 31
        targetSdk = 37
        // The release workflow passes both from the tag and its run number, which only ever increases.
        versionCode = providers.gradleProperty("versionCode").orNull?.toInt() ?: 1
        versionName = providers.gradleProperty("versionName").orNull ?: "0.0.0-dev"
    }

    signingConfigs {
        if (releaseKeystore != null) {
            create("release") {
                storeFile = file(releaseKeystore)
                storeType = "pkcs12"
                storePassword = providers.environmentVariable("EDGEPAD_KEYSTORE_PASSWORD").get()
                keyAlias = providers.environmentVariable("EDGEPAD_KEY_ALIAS").get()
                // A PKCS12 keystore has one password for the store and the key.
                keyPassword = storePassword
            }
        }
    }

    buildTypes {
        release {
            signingConfig = signingConfigs.findByName("release")
        }
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
