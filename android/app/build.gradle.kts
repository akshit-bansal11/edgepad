plugins {
    id("com.android.application")
    id("org.jlleitschuh.gradle.ktlint")
}

// A release build is always unsigned. .github/workflows/release.yml signs it afterwards with apksigner, in a
// job of its own, so the key is never on a machine where Gradle and the plugins it downloads are running.

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
        // Settings shows it; a generated string avoids PackageManager's API-33 split for reading it back.
        resValue("string", "app_version", versionName ?: "")
    }

    buildTypes {
        release {
            // No signingConfig, on purpose: see the note above android {}.
        }
    }

    buildFeatures {
        // The version string Settings shows comes from resValue, which AGP 9 leaves off by default.
        resValues = true
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
    // Renders the player logos from their SVGs in their own colours; the platform has no SVG renderer.
    implementation("com.caverock:androidsvg-aar:1.4")
    testImplementation("junit:junit:4.13.2")
}
