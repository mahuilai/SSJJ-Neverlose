#pragma once
#include <string>

enum CommandType {
    CmdNone = 0,
    CmdWeapon = 1,
    CmdCharacter = 2,
    CmdAccessory = 3,
    CmdScale = 4,
    CmdHead = 5,
    CmdTeam = 6,
    CmdAlpha = 7,
    CmdSelfAlpha = 8,
    CmdApplyAll = 9,
    CmdRestore = 10,
    CmdBlur = 11,
    CmdVisualToggle = 12,
    CmdVisualExposure = 13,
    CmdVisualBloom = 14,
    CmdVisualFog = 15,
    CmdVisualShafts = 16,
    CmdVisualVignette = 17,
    CmdVisualRestore = 18,
    // Extensions for PNG & CSGO HUD
    CmdSwapPng = 30,
    CmdMapInject = 31,
    CmdCsgoHudToggle = 32,
    CmdCsgoHudSound = 33,
    CmdCsgoHudVolume = 34,
    CmdCsgoHudKillCard = 35,
    CmdCsgoHudSuppress = 36,
    CmdCsgoHudTestKill = 37,
    // Extensions for 雨爱 Visual & Skybox Filter
    CmdSkyboxToggle = 40,
    CmdSkyboxR = 41,
    CmdSkyboxG = 42,
    CmdSkyboxB = 43,
    CmdSkyboxIntensity = 44,
    CmdSkyboxLerp = 45,
    CmdSkyboxLerp2 = 46,
    CmdSnowCount = 47,
    CmdSnowSpeed = 48,
    CmdSnowSize = 49,
    CmdFilterPreset = 50,
    CmdThirdPerson = 51,
    CmdThirdPersonDistance = 52,
    CmdThirdPersonFov = 53,
    CmdWorldSnowToggle = 54,
    CmdWorldSnowDensity = 55,
    CmdWorldSnowSpeed = 56,
    CmdWorldSnowSize = 57
};

struct UiCommand {
    int type{};
    float value{};
    std::wstring text;
};
