import XCTest

@testable import EdgepadCore

final class DispatcherTests: XCTestCase {
    private let input = FakeInput()
    private let speakers = FakeLevel(level: Level(percent: 50, muted: false))
    private let microphone = FakeLevel(level: Level(percent: 50, muted: false))
    private let brightness = FakeBrightness(level: 50)
    private let display = FakeDisplay(rates: [60, 120], current: 0)

    private lazy var dispatcher = Dispatcher(
        input: input,
        speakers: speakers,
        microphone: microphone,
        brightness: brightness,
        display: display
    )

    func testAnUnknownActionIsDroppedAndCounted() {
        dispatcher.handle(.runAction(id: 99))

        XCTAssertEqual(dispatcher.dropped, 1)
        XCTAssertEqual(input.calls, [])
    }

    func testTheFirstMacroSlotIsDropped() {
        dispatcher.handle(.runAction(id: ActionId.macroBase.rawValue))

        XCTAssertEqual(dispatcher.dropped, 1)
    }

    func testTheLastMacroSlotIsDropped() {
        dispatcher.handle(.runAction(id: ActionId.macroBase.rawValue + UInt8(ActionId.macroSlots) - 1))

        XCTAssertEqual(dispatcher.dropped, 1)
    }

    private func macroStore(launcher: FakeLauncher, names: [String]) -> MacroStore {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("edgepad-dispatch-\(UUID())")
        addTeardownBlock { try? FileManager.default.removeItem(at: folder) }
        let path = folder.appendingPathComponent("macros.txt").path
        let store = MacroStore(path: path, launcher: launcher)
        store.save(names.map { Macro(name: $0, target: $0 + ".app") })
        return store
    }

    func testAFilledSlotLaunchesTheStoredTargetAndNothingElse() {
        let launcher = FakeLauncher()
        dispatcher.macros = macroStore(launcher: launcher, names: ["Mail", "Code"])

        dispatcher.handle(.runAction(id: ActionId.macroBase.rawValue + 1))

        XCTAssertEqual(launcher.launched, ["Code.app|-"])
        XCTAssertEqual(dispatcher.dropped, 0)
    }

    func testAnEmptySlotIsDroppedAndCounted() {
        let launcher = FakeLauncher()
        dispatcher.macros = macroStore(launcher: launcher, names: ["Mail"])

        dispatcher.handle(.runAction(id: ActionId.macroBase.rawValue + 1))
        dispatcher.handle(.runAction(id: ActionId.macroBase.rawValue + UInt8(ActionId.macroSlots) - 1))

        XCTAssertEqual(launcher.launched, [])
        XCTAssertEqual(dispatcher.dropped, 2)
    }

    func testAnIdBelowTheMacroBlockIsDropped() {
        dispatcher.handle(.runAction(id: ActionId.macroBase.rawValue - 1))

        XCTAssertEqual(dispatcher.dropped, 1)
    }

    func testAnUnknownMouseButtonIsDropped() {
        dispatcher.handle(.pointerButton(id: 3, down: true))

        XCTAssertEqual(dispatcher.dropped, 1)
        XCTAssertEqual(input.calls, [])
    }

    func testTheMiddleMouseButtonReachesTheInput() {
        dispatcher.handle(.pointerButton(id: 2, down: true))

        XCTAssertEqual(dispatcher.dropped, 0)
        XCTAssertEqual(input.calls, ["button 2 true"])
    }

    func testMovementReachesTheInput() {
        dispatcher.handle(.move(dx: 5, dy: -3))

        XCTAssertEqual(input.calls, ["move 5 -3"])
    }

    func testScrollReachesTheInput() {
        dispatcher.handle(.scroll(dx: 0, dy: 120))

        XCTAssertEqual(input.calls, ["scroll 0 120"])
    }

    func testAValueAboveOneHundredIsDropped() {
        dispatcher.handle(.setValue(control: ControlId.volume.rawValue, value: 101))

        XCTAssertEqual(dispatcher.dropped, 1)
        XCTAssertEqual(speakers.sets, [])
    }

    func testAnUnknownControlIsDropped() {
        dispatcher.handle(.setValue(control: 9, value: 50))

        XCTAssertEqual(dispatcher.dropped, 1)
    }

    func testSettingTheVolumeReachesTheSpeakers() {
        dispatcher.handle(.setValue(control: ControlId.volume.rawValue, value: 40))

        XCTAssertEqual(speakers.sets, [40])
        XCTAssertEqual(dispatcher.dropped, 0)
    }

    func testSettingAMissingMicrophoneIsDropped() {
        microphone.accepts = false

        dispatcher.handle(.setValue(control: ControlId.micLevel.rawValue, value: 40))

        XCTAssertEqual(dispatcher.dropped, 1)
    }

    func testSeekingIsDropped() {
        dispatcher.handle(.setValue(control: ControlId.mediaPosition.rawValue, value: 50))

        XCTAssertEqual(dispatcher.dropped, 1)
    }

    func testARefreshRateIndexOutOfRangeIsDropped() {
        dispatcher.handle(.setValue(control: ControlId.refreshRate.rawValue, value: 2))

        XCTAssertEqual(dispatcher.dropped, 1)
        XCTAssertEqual(display.selected, [])
    }

    func testARefreshRateIndexInRangeIsSelected() {
        dispatcher.handle(.setValue(control: ControlId.refreshRate.rawValue, value: 1))

        XCTAssertEqual(display.selected, [1])
        XCTAssertEqual(dispatcher.dropped, 0)
    }

    func testSteppingTheAppSwitcherForwardWithoutOpeningItIsDropped() {
        dispatcher.handle(.runAction(id: ActionId.appSwitchNext.rawValue))

        XCTAssertEqual(dispatcher.dropped, 1)
        XCTAssertEqual(input.calls, [])
    }

    func testSteppingTheAppSwitcherBackWithoutOpeningItIsDropped() {
        dispatcher.handle(.runAction(id: ActionId.appSwitchPrevious.rawValue))

        XCTAssertEqual(dispatcher.dropped, 1)
        XCTAssertEqual(input.calls, [])
    }

    func testTheAppSwitcherHoldsCommandFromBeginToEnd() {
        dispatcher.handle(.runAction(id: ActionId.appSwitchBegin.rawValue))
        dispatcher.handle(.runAction(id: ActionId.appSwitchNext.rawValue))
        dispatcher.handle(.runAction(id: ActionId.appSwitchEnd.rawValue))

        XCTAssertEqual(input.calls, ["hold 55", "chord 48", "chord 48", "release 55"])
        XCTAssertFalse(input.isHeld(.command))
        XCTAssertEqual(dispatcher.dropped, 0)
    }

    func testSteppingTheAppSwitcherBackAddsShift() {
        dispatcher.handle(.runAction(id: ActionId.appSwitchBegin.rawValue))
        dispatcher.handle(.runAction(id: ActionId.appSwitchPrevious.rawValue))

        XCTAssertEqual(input.calls.last, "chord 56,48")
    }

    func testZoomUnderOneStepSendsNothing() {
        dispatcher.handle(.zoom(delta: 119))

        XCTAssertEqual(input.calls, [])
    }

    func testZoomReachingOneStepSendsCommandPlus() {
        dispatcher.handle(.zoom(delta: 119))
        dispatcher.handle(.zoom(delta: 1))

        XCTAssertEqual(input.calls, ["chord 55,24"])
    }

    func testNegativeZoomSendsCommandMinus() {
        dispatcher.handle(.zoom(delta: -120))

        XCTAssertEqual(input.calls, ["chord 55,27"])
    }

    func testZoomResetClearsWhatWasOwed() {
        dispatcher.handle(.zoom(delta: 119))
        dispatcher.handle(.runAction(id: ActionId.zoomReset.rawValue))
        dispatcher.handle(.zoom(delta: 1))

        XCTAssertEqual(input.calls, ["chord 55,29"])
    }

    func testBrightnessUpSetsTenMore() {
        dispatcher.handle(.runAction(id: ActionId.brightnessUp.rawValue))

        XCTAssertEqual(brightness.sets, [60])
    }

    func testBrightnessUpIsClampedAtOneHundred() {
        brightness.level = 95

        dispatcher.handle(.runAction(id: ActionId.brightnessUp.rawValue))

        XCTAssertEqual(brightness.sets, [100])
    }

    func testBrightnessDownIsClampedAtZero() {
        brightness.level = 4

        dispatcher.handle(.runAction(id: ActionId.brightnessDown.rawValue))

        XCTAssertEqual(brightness.sets, [0])
    }

    func testBrightnessUpWithNoReadableLevelSendsTheBrightnessKey() {
        brightness.level = nil

        dispatcher.handle(.runAction(id: ActionId.brightnessUp.rawValue))

        XCTAssertEqual(input.calls, ["media 2"])
        XCTAssertEqual(dispatcher.dropped, 0)
    }

    func testAskingForTheControllerIsAnsweredWithNoDriver() {
        var answers: [String] = []
        dispatcher.padStatusReply = { answers.append($0) }

        dispatcher.handle(.runAction(id: ActionId.padAttach.rawValue))

        XCTAssertEqual(answers, ["no-driver"])
        XCTAssertEqual(dispatcher.dropped, 0)
    }

    func testAControllerFrameIsDropped() {
        dispatcher.handle(.padState(buttons: 1, lt: 0, rt: 0, lx: 0, ly: 0, rx: 0, ry: 0))

        XCTAssertEqual(dispatcher.dropped, 1)
    }

    func testAPenPointAndAPenColourAreDroppedUntilTheMacHasAnOverlay() {
        // State 7 decodes fine and is dropped here: a newer phone may add states.
        dispatcher.handle(.pen(x: 0, y: 0, state: 1))
        dispatcher.handle(.pen(x: 1, y: 2, state: 7))
        dispatcher.handle(.setValue(control: ControlId.penColor.rawValue, value: 6))

        XCTAssertEqual(dispatcher.dropped, 3)
    }

    func testTheAnnotateAndSignActionsAreDropped() {
        for id in UInt8(40)...UInt8(51) {
            dispatcher.handle(.runAction(id: id))
        }

        XCTAssertEqual(dispatcher.dropped, 12)
    }

    func testWithAnOverlayThePenColourAndEveryAnnotateActionReachIt() {
        let annotation = FakeAnnotation()
        dispatcher.annotation = annotation

        dispatcher.handle(.pen(x: 1, y: 2, state: 1))
        dispatcher.handle(.setValue(control: ControlId.penColor.rawValue, value: 6))
        for id in UInt8(40)...UInt8(51) {
            dispatcher.handle(.runAction(id: id))
        }

        XCTAssertEqual(dispatcher.dropped, 0)
        XCTAssertEqual(annotation.calls.count, 14)
        XCTAssertEqual(Array(annotation.calls.prefix(3)), ["pen 1 2 1", "colour 6", "run 40"])
        XCTAssertEqual(annotation.calls.last, "run 51")
    }

    func testWhatTheOverlayRefusesIsCountedAsDropped() {
        let annotation = FakeAnnotation()
        annotation.accepts = false
        dispatcher.annotation = annotation

        dispatcher.handle(.pen(x: 0, y: 0, state: 9))
        dispatcher.handle(.runAction(id: ActionId.toolArrow.rawValue))
        dispatcher.handle(.setValue(control: ControlId.penColor.rawValue, value: 8))

        XCTAssertEqual(dispatcher.dropped, 3)
    }

    func testCaptureActionsStillPressTheSystemShortcutsWithAnOverlay() {
        dispatcher.annotation = FakeAnnotation()

        dispatcher.handle(.runAction(id: ActionId.captureRegion.rawValue))

        XCTAssertEqual(dispatcher.dropped, 0)
        XCTAssertEqual(input.calls.count, 1)
    }

    func testNotificationsAreDropped() {
        dispatcher.handle(.runAction(id: ActionId.notifications.rawValue))

        XCTAssertEqual(dispatcher.dropped, 1)
    }

    func testMutingTheMicrophoneTogglesIt() {
        dispatcher.handle(.runAction(id: ActionId.micMuteToggle.rawValue))

        XCTAssertEqual(microphone.muteToggles, 1)
    }

    func testFramesOnlyTheMacSendsAreDropped() {
        dispatcher.handle(.pong(time: 1))
        dispatcher.handle(.state(control: 0, value: 0, flags: 0))
        dispatcher.handle(.helloAck(version: 4))

        XCTAssertEqual(dispatcher.dropped, 3)
    }

    func testAMappedKeyReachesTheInput() {
        dispatcher.handle(.key(code: 0x41, down: true))

        XCTAssertEqual(input.calls, ["key 0 true"])
    }

    func testAnUnmappedKeyIsDropped() {
        dispatcher.handle(.key(code: 0xE9, down: true))

        XCTAssertEqual(dispatcher.dropped, 1)
        XCTAssertEqual(input.calls, [])
    }

    func testAMediaKeyFiresOnDown() {
        dispatcher.handle(.key(code: 0xB3, down: true))

        XCTAssertEqual(input.calls, ["media 16"])
    }

    func testAMediaKeyDoesNotFireOnUp() {
        dispatcher.handle(.key(code: 0xB3, down: false))

        XCTAssertEqual(input.calls, [])
        XCTAssertEqual(dispatcher.dropped, 0)
    }

    func testTextOfKindTypeIsTyped() {
        dispatcher.handle(.text(kind: TextKind.type.rawValue, value: "hi"))

        XCTAssertEqual(input.calls, ["type hi"])
    }

    func testTextOfAnotherKindIsDropped() {
        dispatcher.handle(.text(kind: TextKind.app.rawValue, value: "x"))

        XCTAssertEqual(dispatcher.dropped, 1)
        XCTAssertEqual(input.calls, [])
    }

    func testLockSendsControlCommandQAsOneChord() {
        dispatcher.handle(.runAction(id: ActionId.lock.rawValue))

        XCTAssertEqual(input.calls, ["chord 59,55,12"])
        XCTAssertEqual(dispatcher.dropped, 0)
    }

    func testTaskViewSendsControlUp() {
        dispatcher.handle(.runAction(id: ActionId.taskView.rawValue))

        XCTAssertEqual(input.calls, ["chord 59,126"])
        XCTAssertEqual(dispatcher.dropped, 0)
    }

    func testShowDesktopSendsF11() {
        dispatcher.handle(.runAction(id: ActionId.showDesktop.rawValue))

        XCTAssertEqual(input.calls, ["chord 103"])
        XCTAssertEqual(dispatcher.dropped, 0)
    }

    func testSearchSendsCommandSpace() {
        dispatcher.handle(.runAction(id: ActionId.search.rawValue))

        XCTAssertEqual(input.calls, ["chord 55,49"])
        XCTAssertEqual(dispatcher.dropped, 0)
    }

    func testDesktopLeftSendsControlLeft() {
        dispatcher.handle(.runAction(id: ActionId.desktopLeft.rawValue))

        XCTAssertEqual(input.calls, ["chord 59,123"])
        XCTAssertEqual(dispatcher.dropped, 0)
    }

    func testDesktopRightSendsControlRight() {
        dispatcher.handle(.runAction(id: ActionId.desktopRight.rawValue))

        XCTAssertEqual(input.calls, ["chord 59,124"])
        XCTAssertEqual(dispatcher.dropped, 0)
    }

    func testZoomResetSendsCommandZero() {
        dispatcher.handle(.runAction(id: ActionId.zoomReset.rawValue))

        XCTAssertEqual(input.calls, ["chord 55,29"])
        XCTAssertEqual(dispatcher.dropped, 0)
    }

    func testCaptureSendsTheSystemsOwnShortcuts() {
        // Command is 55, Shift 56; the digits 3, 4 and 5 are 20, 21 and 23.
        dispatcher.handle(.runAction(id: ActionId.captureRegion.rawValue))
        dispatcher.handle(.runAction(id: ActionId.captureScreen.rawValue))
        dispatcher.handle(.runAction(id: ActionId.captureRecord.rawValue))

        XCTAssertEqual(input.calls, ["chord 55,56,21", "chord 55,56,20", "chord 55,56,23"])
        XCTAssertEqual(dispatcher.dropped, 0)
    }

    func testMuteSendsTheMuteKey() {
        dispatcher.handle(.runAction(id: ActionId.muteToggle.rawValue))

        XCTAssertEqual(input.calls, ["media 7"])
        XCTAssertEqual(dispatcher.dropped, 0)
    }

    func testPlayPauseSendsThePlayKey() {
        dispatcher.handle(.runAction(id: ActionId.playPause.rawValue))

        XCTAssertEqual(input.calls, ["media 16"])
        XCTAssertEqual(dispatcher.dropped, 0)
    }

    func testNextSendsTheNextKey() {
        dispatcher.handle(.runAction(id: ActionId.nextTrack.rawValue))

        XCTAssertEqual(input.calls, ["media 17"])
        XCTAssertEqual(dispatcher.dropped, 0)
    }

    func testPreviousSendsThePreviousKey() {
        dispatcher.handle(.runAction(id: ActionId.previousTrack.rawValue))

        XCTAssertEqual(input.calls, ["media 18"])
        XCTAssertEqual(dispatcher.dropped, 0)
    }

    func testVolumeUpSendsTheSoundUpKey() {
        dispatcher.handle(.runAction(id: ActionId.volumeUp.rawValue))

        XCTAssertEqual(input.calls, ["media 0"])
        XCTAssertEqual(dispatcher.dropped, 0)
    }

    func testVolumeDownSendsTheSoundDownKey() {
        dispatcher.handle(.runAction(id: ActionId.volumeDown.rawValue))

        XCTAssertEqual(input.calls, ["media 1"])
        XCTAssertEqual(dispatcher.dropped, 0)
    }
}
