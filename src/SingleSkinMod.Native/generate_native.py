# -*- coding: utf-8 -*-
import os

cpp_code = r'''#include <windows.h>
#include <d3d11.h>
#include <dxgi1_2.h>
#include <d3dcompiler.h>
#include <atomic>
#include <chrono>
#include <deque>
#include <mutex>
#include <string>
#include <vector>
#include <algorithm>
#include <cmath>

#define IMGUI_DEFINE_MATH_OPERATORS
#include "MinHook.h"
#include "imgui.h"
#include "imgui_internal.h"
#include "imgui_impl_win32.h"
#include "imgui_impl_dx11.h"
#include "gui.hpp"
#include "hashes.hpp"
#include "bytes.hpp"
#include "shared_bridge.h"

extern IMGUI_IMPL_API LRESULT ImGui_ImplWin32_WndProcHandler(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam);

// ===== Global State =====
static std::atomic<bool> g_hookReady{ false };
static std::atomic<bool> g_hookStarting{ false };
static std::atomic<bool> g_rendererReady{ false };
static std::atomic<bool> g_menuVisible{ false };
static std::atomic<bool> g_shuttingDown{ false };

using ManagedTickFn = void(__cdecl*)();
static ManagedTickFn g_managedTick = nullptr;

static HWND g_window = nullptr;
static WNDPROC g_originalWndProc = nullptr;
static ID3D11Device* g_d3d11Device = nullptr;
static ID3D11DeviceContext* g_d3d11Context = nullptr;
static ID3D11RenderTargetView* g_renderTargetView = nullptr;
static IDXGISwapChain* g_swapChain = nullptr;

using PresentFn = HRESULT(__stdcall*)(IDXGISwapChain*, UINT, UINT);
using Present1Fn = HRESULT(__stdcall*)(IDXGISwapChain1*, UINT, UINT, const DXGI_PRESENT_PARAMETERS*);
using ResizeBuffersFn = HRESULT(__stdcall*)(IDXGISwapChain*, UINT, UINT, UINT, DXGI_FORMAT, UINT);

static PresentFn g_originalPresent = nullptr;
static Present1Fn g_originalPresent1 = nullptr;
static ResizeBuffersFn g_originalResize = nullptr;

static std::mutex g_stateMutex;
static std::mutex g_queueMutex;
static std::deque<UiCommand> g_commandQueue;

// Catalogs: 0=Weapon, 1=Character, 2=Accessory
static std::vector<std::wstring> g_catalogs[3];
static std::wstring g_selections[3];
// Numeric: 0=Scale, 1=HeadScale, 2=Team, 3=Alpha, 4=SelfAlpha, 5=Blur
static float g_numeric[6] = { 1.0f, 1.0f, 0.0f, 100.0f, 100.0f, 0.0f };
static std::wstring g_status = L"就绪 (Ready)";

// Visual post-process
static bool g_visualEnabled = false;
static float g_visualExposure = 0.0f;
static float g_visualBloom = 0.0f;
static float g_visualFog = 0.0f;
static float g_visualShafts = 0.0f;
static float g_visualVignette = 0.0f;

// CSGO HUD states
static bool g_csgoHudEnabled = true;
static bool g_csgoHudSound = true;
static float g_csgoHudVolume = 0.8f;
static bool g_csgoHudKillCard = true;
static bool g_csgoHudSuppress = true;

// 雨爱 Skybox & Snow visual states
static bool g_skyboxEnabled = false;
static float g_skyboxR = 0.8f;
static float g_skyboxG = 0.4f;
static float g_skyboxB = 1.0f;
static float g_skyboxIntensity = 1.0f;
static float g_skyboxLerp = 1.0f;
static float g_skyboxLerp2 = 0.5f;
static bool g_snowEnabled = false;
static float g_snowCount = 80.0f;
static float g_snowSpeed = 60.0f;
static float g_snowSize = 4.0f;
static int g_filterPreset = 0;

// UI inputs for PNG Swap
static char g_pngKeyword[128] = "weapon";
static char g_pngFileName[128] = "custom.png";
static char g_mapFolder[128] = "map1";

// Subtab states for Neverlose tabs
static int g_subtabs[4] = { 0, 0, 0, 0 };
static char g_weaponSearch[64] = "";

static void QueueCommand(int type, float val = 0.0f, const std::wstring& txt = L"") {
    std::lock_guard<std::mutex> lock(g_queueMutex);
    g_commandQueue.push_back({ type, val, txt });
    if (g_commandQueue.size() > 256) g_commandQueue.pop_front();
}

static std::wstring Utf8ToWstring(const std::string& str) {
    if (str.empty()) return L"";
    int size = MultiByteToWideChar(CP_UTF8, 0, str.c_str(), (int)str.size(), nullptr, 0);
    std::wstring result(size, 0);
    MultiByteToWideChar(CP_UTF8, 0, str.c_str(), (int)str.size(), &result[0], size);
    return result;
}

static std::string WstringToUtf8(const std::wstring& wstr) {
    if (wstr.empty()) return "";
    int size = WideCharToMultiByte(CP_UTF8, 0, wstr.c_str(), (int)wstr.size(), nullptr, 0, nullptr, nullptr);
    std::string result(size, 0);
    WideCharToMultiByte(CP_UTF8, 0, wstr.c_str(), (int)wstr.size(), &result[0], size, nullptr, nullptr);
    return result;
}

static void CreateRenderTarget(IDXGISwapChain* swapChain) {
    ID3D11Texture2D* backBuffer = nullptr;
    if (SUCCEEDED(swapChain->GetBuffer(0, IID_PPV_ARGS(&backBuffer)))) {
        g_d3d11Device->CreateRenderTargetView(backBuffer, nullptr, &g_renderTargetView);
        backBuffer->Release();
    }
}

static void CleanupRenderTarget() {
    if (g_renderTargetView) {
        g_renderTargetView->Release();
        g_renderTargetView = nullptr;
    }
}

static LRESULT CALLBACK HookWndProc(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam) {
    if (msg == WM_KEYDOWN) {
        if (wParam == VK_HOME || wParam == VK_F12) {
            g_menuVisible = !g_menuVisible;
            return 0;
        }
        if (wParam == VK_F5) {
            QueueCommand(CmdApplyAll);
            return 0;
        }
        if (wParam == VK_F6) {
            QueueCommand(CmdRestore);
            return 0;
        }
    }

    if (g_menuVisible) {
        ImGui_ImplWin32_WndProcHandler(hWnd, msg, wParam, lParam);
        switch (msg) {
            case WM_LBUTTONDOWN: case WM_LBUTTONUP:
            case WM_RBUTTONDOWN: case WM_RBUTTONUP:
            case WM_MOUSEMOVE: case WM_MOUSEWHEEL:
            case WM_CHAR:
                return 0;
        }
    }

    return CallWindowProcW(g_originalWndProc, hWnd, msg, wParam, lParam);
}

// ===== Neverlose Fonts & Styling =====
static void SetupNeverloseStyle() {
    ImGuiStyle& s = ImGui::GetStyle();
    s.WindowRounding = 6.0f;
    s.ChildRounding = 4.0f;
    s.FrameRounding = 4.0f;
    s.PopupRounding = 4.0f;
    s.GrabRounding = 4.0f;
    s.ScrollbarRounding = 4.0f;
    s.WindowBorderSize = 1.0f;
    s.FrameBorderSize = 0.0f;
    s.WindowPadding = ImVec2(0.0f, 0.0f);
    s.FramePadding = ImVec2(8.0f, 4.0f);
    s.ItemSpacing = ImVec2(8.0f, 8.0f);
    s.ItemInnerSpacing = ImVec2(6.0f, 4.0f);
    s.ScrollbarSize = 6.0f;

    ImVec4* colors = s.Colors;
    colors[ImGuiCol_Text] = gui.text.to_im_color();
    colors[ImGuiCol_TextDisabled] = gui.text_disabled.to_im_color();
    colors[ImGuiCol_WindowBg] = ImVec4(0.02f, 0.035f, 0.06f, 0.98f);
    colors[ImGuiCol_ChildBg] = ImVec4(0.015f, 0.028f, 0.05f, 0.85f);
    colors[ImGuiCol_PopupBg] = ImVec4(0.02f, 0.035f, 0.06f, 0.98f);
    colors[ImGuiCol_Border] = gui.border.to_im_color();
    colors[ImGuiCol_BorderShadow] = ImVec4(0.0f, 0.0f, 0.0f, 0.0f);
    colors[ImGuiCol_FrameBg] = gui.frame_inactive.to_im_color();
    colors[ImGuiCol_FrameBgHovered] = gui.frame_active.to_im_color();
    colors[ImGuiCol_FrameBgActive] = gui.frame_active.to_im_color();
    colors[ImGuiCol_TitleBg] = ImVec4(0.015f, 0.025f, 0.045f, 1.0f);
    colors[ImGuiCol_TitleBgActive] = ImVec4(0.015f, 0.025f, 0.045f, 1.0f);
    colors[ImGuiCol_Button] = gui.button.to_im_color();
    colors[ImGuiCol_ButtonHovered] = gui.button_hovered.to_im_color();
    colors[ImGuiCol_ButtonActive] = gui.button_active.to_im_color();
    colors[ImGuiCol_Header] = gui.button.to_im_color();
    colors[ImGuiCol_HeaderHovered] = gui.button_hovered.to_im_color();
    colors[ImGuiCol_HeaderActive] = gui.button_active.to_im_color();
    colors[ImGuiCol_Separator] = gui.border.to_im_color();
    colors[ImGuiCol_SliderGrab] = gui.accent_color.to_im_color();
    colors[ImGuiCol_SliderGrabActive] = ImVec4(0.45f, 0.65f, 1.0f, 1.0f);
    colors[ImGuiCol_CheckMark] = gui.accent_color.to_im_color();
    colors[ImGuiCol_ScrollbarBg] = ImVec4(0.01f, 0.018f, 0.03f, 0.5f);
    colors[ImGuiCol_ScrollbarGrab] = gui.button_hovered.to_im_color();
    colors[ImGuiCol_ScrollbarGrabHovered] = gui.button_active.to_im_color();
    colors[ImGuiCol_ScrollbarGrabActive] = gui.accent_color.to_im_color();
}

static void LoadNeverloseFonts() {
    ImGuiIO& io = ImGui::GetIO();
    io.Fonts->Clear();

    // 1. Chinese Font as primary
    const char* fonts[] = {
        "C:\\Windows\\Fonts\\msyh.ttc",
        "C:\\Windows\\Fonts\\msyh.ttf",
        "C:\\Windows\\Fonts\\simhei.ttf"
    };
    bool baseLoaded = false;
    for (const char* path : fonts) {
        if (GetFileAttributesA(path) != INVALID_FILE_ATTRIBUTES) {
            io.Fonts->AddFontFromFileTTF(path, 15.0f, nullptr, io.Fonts->GetGlyphRangesChineseFull());
            baseLoaded = true;
            break;
        }
    }
    if (!baseLoaded) {
        io.Fonts->AddFontDefault();
    }

    // 2. Merge FontAwesome
    static const ImWchar icon_ranges[] = { ICON_MIN_FA, ICON_MAX_FA, 0 };
    ImFontConfig icons_config;
    icons_config.MergeMode = true;
    icons_config.PixelSnapH = true;
    io.Fonts->AddFontFromMemoryTTF((void*)font_awesome_binary, sizeof(font_awesome_binary), 13.0f, &icons_config, icon_ranges);

    // 3. Title font (Museo 900)
    io.Fonts->AddFontFromMemoryTTF((void*)museo900_binary, sizeof(museo900_binary), 22.0f);
}

// ===== Draw Neverlose Menu =====
static void DrawNeverloseMenu() {
    ImGui::SetNextWindowSize(ImVec2(780.0f, 530.0f), ImGuiCond_FirstUseEver);
    ImGui::SetNextWindowPos(ImVec2(120.0f, 100.0f), ImGuiCond_FirstUseEver);

    ImGui::PushStyleVar(ImGuiStyleVar_WindowPadding, ImVec2(0, 0));
    bool open = true;
    if (!ImGui::Begin("##NeverloseMain", &open, ImGuiWindowFlags_NoDecoration | ImGuiWindowFlags_NoBackground)) {
        ImGui::PopStyleVar();
        ImGui::End();
        return;
    }

    auto window = ImGui::GetCurrentWindow();
    auto draw = window->DrawList;
    auto pos = window->Pos;
    auto size = window->Size;

    gui.m_anim = ImLerp(gui.m_anim, 1.0f, 0.06f);

    // Background and Outer Border
    draw->AddRectFilled(pos, pos + size, ImColor(8, 14, 24, 250), 8.0f);
    draw->AddRect(pos, pos + size, gui.border.to_im_color(0.5f), 8.0f);

    // Sidebar vertical background & line
    draw->AddRectFilled(pos, pos + ImVec2(170.0f, size.y), ImColor(5, 9, 16, 255), 8.0f, ImDrawFlags_RoundCornersLeft);
    draw->AddLine(pos + ImVec2(170.0f, 0), pos + ImVec2(170.0f, size.y), gui.border.to_im_color());

    // Neverlose glowing title logo
    ImFont* titleFont = ImGui::GetIO().Fonts->Fonts.size() > 1 ? ImGui::GetIO().Fonts->Fonts[1] : ImGui::GetFont();
    const char* logoText = "NEVERLOSE";
    float titleSize = 22.0f;
    ImVec2 logoSize = titleFont->CalcTextSizeA(titleSize, FLT_MAX, 0.0f, logoText);
    draw->AddText(titleFont, titleSize, pos + ImVec2(85.0f - logoSize.x / 2.0f + 1, 16.0f), gui.accent_color.to_im_color(), logoText);
    draw->AddText(titleFont, titleSize, pos + ImVec2(85.0f - logoSize.x / 2.0f, 16.0f), gui.text.to_im_color(), logoText);

    // Sidebar navigation tabs
    ImGui::SetCursorPos(ImVec2(10.0f, 55.0f));
    ImGui::BeginChild("##SidebarTabs", ImVec2(150.0f, size.y - 120.0f), false, ImGuiWindowFlags_NoScrollbar);
    {
        gui.group_title("CUSTOMIZATION");
        if (gui.tab(ICON_FA_SHIELD_ALT, "Skins & Maps", gui.m_tab == 0) && gui.m_tab != 0) {
            gui.m_tab = 0; gui.m_anim = 0.0f;
        }

        ImGui::Spacing(); ImGui::Spacing();
        gui.group_title("ATMOSPHERE");
        if (gui.tab(ICON_FA_PALETTE, "Visual & Weather", gui.m_tab == 1) && gui.m_tab != 1) {
            gui.m_tab = 1; gui.m_anim = 0.0f;
        }

        ImGui::Spacing(); ImGui::Spacing();
        gui.group_title("COMBAT INTERFACE");
        if (gui.tab(ICON_FA_CROSSHAIRS, "Combat HUD", gui.m_tab == 2) && gui.m_tab != 2) {
            gui.m_tab = 2; gui.m_anim = 0.0f;
        }

        ImGui::Spacing(); ImGui::Spacing();
        gui.group_title("CONFIGURATION");
        if (gui.tab(ICON_FA_SLIDERS_H, "Player & Settings", gui.m_tab == 3) && gui.m_tab != 3) {
            gui.m_tab = 3; gui.m_anim = 0.0f;
        }
    }
    ImGui::EndChild();

    // Sidebar user/status footer
    draw->AddLine(pos + ImVec2(0, size.y - 52.0f), pos + ImVec2(170.0f, size.y - 52.0f), gui.border.to_im_color());
    draw->AddCircleFilled(pos + ImVec2(24.0f, size.y - 26.0f), 10.0f, gui.accent_color.to_im_color(0.3f));
    draw->AddCircleFilled(pos + ImVec2(24.0f, size.y - 26.0f), 5.0f, gui.accent_color.to_im_color());
    draw->AddText(pos + ImVec2(42.0f, size.y - 36.0f), gui.text.to_im_color(), "SSJJ SingleSkin");
    {
        std::lock_guard<std::mutex> lock(g_stateMutex);
        std::string st = WstringToUtf8(g_status);
        if (st.length() > 14) st = st.substr(0, 14) + "..";
        draw->AddText(pos + ImVec2(42.0f, size.y - 20.0f), gui.text_disabled.to_im_color(), st.c_str());
    }

    // Top action bar (Quick Apply F5 / Restore F6 & Subtabs)
    ImGui::SetCursorPos(ImVec2(185.0f, 14.0f));
    if (ImGui::Button(ICON_FA_SAVE " 一键应用 (F5)", ImVec2(115.0f, 26.0f))) {
        QueueCommand(CmdApplyAll);
    }
    ImGui::SameLine();
    if (ImGui::Button(ICON_FA_UNDO " 还原 (F6)", ImVec2(90.0f, 26.0f))) {
        QueueCommand(CmdRestore);
    }

    // Subtab definitions for active main tab
    std::vector<const char*> subtabLabels;
    if (gui.m_tab == 0) {
        subtabLabels = { "武器换模", "角色挂件", "自定义PNG" };
    } else if (gui.m_tab == 1) {
        subtabLabels = { "雨爱滤镜", "浪漫飘雪", "后处理增强" };
    } else if (gui.m_tab == 2) {
        subtabLabels = { "CSGO 2 HUD", "打击玻璃音效" };
    } else {
        subtabLabels = { "玩家参数微调", "系统与TCP信息" };
    }

    int activeSub = g_subtabs[gui.m_tab];
    if (activeSub >= (int)subtabLabels.size()) activeSub = 0;

    ImGui::SameLine(ImGui::GetWindowWidth() - 320.0f);
    ImGui::BeginChild("##SubtabBar", ImVec2(305.0f, 26.0f), false, ImGuiWindowFlags_NoScrollbar);
    {
        draw->AddRectFilled(ImGui::GetWindowPos(), ImGui::GetWindowPos() + ImGui::GetWindowSize(), gui.button.to_im_color(), 4.0f);
        draw->AddRect(ImGui::GetWindowPos(), ImGui::GetWindowPos() + ImGui::GetWindowSize(), gui.border.to_im_color(), 4.0f);

        ImGui::PushStyleVar(ImGuiStyleVar_ItemSpacing, ImVec2(0, 0));
        for (size_t i = 0; i < subtabLabels.size(); ++i) {
            ImDrawFlags flags = 0;
            if (i == 0) flags |= ImDrawFlags_RoundCornersLeft;
            if (i == subtabLabels.size() - 1) flags |= ImDrawFlags_RoundCornersRight;

            if (gui.subtab(subtabLabels[i], activeSub == (int)i, (int)subtabLabels.size(), flags) && activeSub != (int)i) {
                g_subtabs[gui.m_tab] = (int)i;
                gui.m_anim = 0.0f;
            }
            if (i != subtabLabels.size() - 1) ImGui::SameLine();
        }
        ImGui::PopStyleVar();
    }
    ImGui::EndChild();

    // Main content area
    float contentW = size.x - 195.0f;
    float contentH = size.y - 62.0f;
    float colW = (contentW - 10.0f) / 2.0f;

    ImGui::SetCursorPos(ImVec2(185.0f, 50.0f));
    ImGui::BeginChild("##MainContent", ImVec2(contentW, contentH), false, ImGuiWindowFlags_NoScrollbar);
    {
        // ===== TAB 0: SKINS & MAPS =====
        if (gui.m_tab == 0) {
            if (activeSub == 0) {
                // Subtab 0: Weapon Swap
                gui.group_box(ICON_FA_CROSSHAIRS " 官方武器模型库 (ECS 热切换)", ImVec2(colW, contentH));
                {
                    ImGui::InputTextWithHint("##WeaponSearch", "输入搜索武器名称...", g_weaponSearch, sizeof(g_weaponSearch));
                    ImGui::Spacing();

                    std::string filter = g_weaponSearch;
                    std::transform(filter.begin(), filter.end(), filter.begin(), ::tolower);

                    ImGui::BeginChild("##WeaponCatalogList", ImVec2(0, contentH - 85.0f), true);
                    std::lock_guard<std::mutex> lock(g_stateMutex);
                    const auto& list = g_catalogs[0];
                    std::string currentSel = WstringToUtf8(g_selections[0]);

                    if (list.empty()) {
                        ImGui::TextDisabled("武器目录扫描中，局内自动就绪...");
                    } else {
                        for (const auto& witem : list) {
                            std::string item = WstringToUtf8(witem);
                            std::string itemLower = item;
                            std::transform(itemLower.begin(), itemLower.end(), itemLower.begin(), ::tolower);
                            if (!filter.empty() && itemLower.find(filter) == std::string::npos) continue;

                            bool isSelected = (currentSel == item);
                            if (ImGui::Selectable(item.c_str(), isSelected)) {
                                g_selections[0] = witem;
                                QueueCommand(CmdWeapon, 0.0f, witem);
                            }
                        }
                    }
                    ImGui::EndChild();
                }
                gui.end_group_box();

                ImGui::SameLine();

                gui.group_box(ICON_FA_CHECK " 当前选定与快速控制", ImVec2(colW, contentH));
                {
                    std::string currentSel;
                    {
                        std::lock_guard<std::mutex> lock(g_stateMutex);
                        currentSel = WstringToUtf8(g_selections[0]);
                    }
                    ImGui::TextColored(gui.accent_color.to_im_color(), "当前选定武器:");
                    ImGui::TextWrapped("%s", currentSel.empty() ? "(未选定)" : currentSel.c_str());
                    ImGui::Spacing(); ImGui::Separator(); ImGui::Spacing();

                    ImGui::TextDisabled("说明: 在局内手持任意武器时，点击左侧列表武器并按 [F5] 即可瞬间零延迟替换成目标模型与特效。");
                    ImGui::Spacing();

                    if (ImGui::Button(ICON_FA_SAVE " 立即应用换模 (F5)", ImVec2(colW - 20.0f, 32.0f))) {
                        QueueCommand(CmdApplyAll);
                    }
                    ImGui::Spacing();
                    if (ImGui::Button(ICON_FA_UNDO " 还原官方原厂模型 (F6)", ImVec2(colW - 20.0f, 32.0f))) {
                        QueueCommand(CmdRestore);
                    }
                }
                gui.end_group_box();
            }
            else if (activeSub == 1) {
                // Subtab 1: Character & Accessory
                gui.group_box(ICON_FA_SHIELD_ALT " 人物角色模型库", ImVec2(colW, contentH));
                {
                    ImGui::BeginChild("##CharCatalogList", ImVec2(0, contentH - 50.0f), true);
                    std::lock_guard<std::mutex> lock(g_stateMutex);
                    const auto& list = g_catalogs[1];
                    std::string currentSel = WstringToUtf8(g_selections[1]);
                    for (const auto& witem : list) {
                        std::string item = WstringToUtf8(witem);
                        if (ImGui::Selectable(item.c_str(), currentSel == item)) {
                            g_selections[1] = witem;
                            QueueCommand(CmdCharacter, 0.0f, witem);
                        }
                    }
                    ImGui::EndChild();
                }
                gui.end_group_box();

                ImGui::SameLine();

                gui.group_box(ICON_FA_MAGIC " 翅膀 / 背部挂件库", ImVec2(colW, contentH));
                {
                    ImGui::BeginChild("##AccCatalogList", ImVec2(0, contentH - 50.0f), true);
                    std::lock_guard<std::mutex> lock(g_stateMutex);
                    const auto& list = g_catalogs[2];
                    std::string currentSel = WstringToUtf8(g_selections[2]);
                    for (const auto& witem : list) {
                        std::string item = WstringToUtf8(witem);
                        if (ImGui::Selectable(item.c_str(), currentSel == item)) {
                            g_selections[2] = witem;
                            QueueCommand(CmdAccessory, 0.0f, witem);
                        }
                    }
                    ImGui::EndChild();
                }
                gui.end_group_box();
            }
            else {
                // Subtab 2: Custom PNG & Map
                gui.group_box(ICON_FA_PAINT_BRUSH " 自定义 PNG 材质热覆盖", ImVec2(colW, contentH));
                {
                    ImGui::Text("材质名关键词 (Keyword):");
                    ImGui::InputText("##PngKeyword", g_pngKeyword, sizeof(g_pngKeyword));
                    ImGui::Spacing();

                    ImGui::Text("PNG 文件名 (SingleSkinMod/skin/):");
                    ImGui::InputText("##PngFile", g_pngFileName, sizeof(g_pngFileName));
                    ImGui::Spacing();

                    if (ImGui::Button("执行材质替换 (DoSwap)", ImVec2(colW - 20.0f, 32.0f))) {
                        std::string payload = std::string(g_pngKeyword) + "|" + g_pngFileName;
                        QueueCommand(CmdSwapPng, 0.0f, Utf8ToWstring(payload));
                    }
                    ImGui::Spacing();
                    ImGui::TextDisabled("自动匹配包含该关键词的材质球并注入PNG纹理。");
                }
                gui.end_group_box();

                ImGui::SameLine();

                gui.group_box(ICON_FA_PALETTE " 地图高清纹理全套批量注入", ImVec2(colW, contentH));
                {
                    ImGui::Text("地图素材子目录 (maps/):");
                    ImGui::InputText("##MapFolder", g_mapFolder, sizeof(g_mapFolder));
                    ImGui::Spacing();

                    if (ImGui::Button("一键注入整套地图贴图", ImVec2(colW - 20.0f, 32.0f))) {
                        QueueCommand(CmdMapInject, 0.0f, Utf8ToWstring(g_mapFolder));
                    }
                    ImGui::Spacing();
                    ImGui::TextDisabled("遍历 maps/ 下所有 PNG 并智能覆盖地表、建筑及天空材质。");
                }
                gui.end_group_box();
            }
        }
        // ===== TAB 1: VISUAL & WEATHER (雨爱光影) =====
        else if (gui.m_tab == 1) {
            if (activeSub == 0) {
                // 雨爱滤镜调色
                gui.group_box(ICON_FA_PALETTE " 雨爱天空盒 / 全图 RGB 调色", ImVec2(colW, contentH));
                {
                    if (ImGui::Checkbox("启用雨爱色彩滤镜", &g_skyboxEnabled)) {
                        QueueCommand(CmdSkyboxToggle, g_skyboxEnabled ? 1.0f : 0.0f);
                    }
                    ImGui::Spacing();

                    if (ImGui::SliderFloat("红色分量 (R)", &g_skyboxR, 0.0f, 1.0f, "%.2f")) {
                        QueueCommand(CmdSkyboxR, g_skyboxR);
                    }
                    if (ImGui::SliderFloat("绿色分量 (G)", &g_skyboxG, 0.0f, 1.0f, "%.2f")) {
                        QueueCommand(CmdSkyboxG, g_skyboxG);
                    }
                    if (ImGui::SliderFloat("蓝色分量 (B)", &g_skyboxB, 0.0f, 1.0f, "%.2f")) {
                        QueueCommand(CmdSkyboxB, g_skyboxB);
                    }
                    if (ImGui::SliderFloat("色彩饱和度 (Intensity)", &g_skyboxIntensity, 0.0f, 2.0f, "%.2f")) {
                        QueueCommand(CmdSkyboxIntensity, g_skyboxIntensity);
                    }
                    if (ImGui::SliderFloat("天空盒融合过渡 (Lerp)", &g_skyboxLerp, 0.0f, 1.0f, "%.2f")) {
                        QueueCommand(CmdSkyboxLerp, g_skyboxLerp);
                    }
                    if (ImGui::SliderFloat("场景暗部融合 (Lerp2)", &g_skyboxLerp2, 0.0f, 1.0f, "%.2f")) {
                        QueueCommand(CmdSkyboxLerp2, g_skyboxLerp2);
                    }
                }
                gui.end_group_box();

                ImGui::SameLine();

                gui.group_box(ICON_FA_MAGIC " 雨爱调色大师预设", ImVec2(colW, contentH));
                {
                    ImGui::TextDisabled("一键载入雨爱项目经典调色配置:");
                    ImGui::Spacing();

                    if (ImGui::Button("1. 赛博紫夜 (Cyber Purple)", ImVec2(colW - 20.0f, 30.0f))) {
                        g_skyboxEnabled = true; g_skyboxR = 0.75f; g_skyboxG = 0.35f; g_skyboxB = 1.0f;
                        g_skyboxIntensity = 1.0f; g_skyboxLerp = 0.85f; g_skyboxLerp2 = 0.6f;
                        QueueCommand(CmdFilterPreset, 1.0f);
                    }
                    ImGui::Spacing();
                    if (ImGui::Button("2. 暮光暖阳 (Sunset Glow)", ImVec2(colW - 20.0f, 30.0f))) {
                        g_skyboxEnabled = true; g_skyboxR = 1.0f; g_skyboxG = 0.6f; g_skyboxB = 0.3f;
                        g_skyboxIntensity = 0.9f; g_skyboxLerp = 0.8f; g_skyboxLerp2 = 0.4f;
                        QueueCommand(CmdFilterPreset, 2.0f);
                    }
                    ImGui::Spacing();
                    if (ImGui::Button("3. 冰晶极地 (Arctic Ice)", ImVec2(colW - 20.0f, 30.0f))) {
                        g_skyboxEnabled = true; g_skyboxR = 0.4f; g_skyboxG = 0.8f; g_skyboxB = 1.0f;
                        g_skyboxIntensity = 1.0f; g_skyboxLerp = 0.9f; g_skyboxLerp2 = 0.5f;
                        QueueCommand(CmdFilterPreset, 3.0f);
                    }
                    ImGui::Spacing();
                    if (ImGui::Button("4. 幽荧暗绿 (Ghostly Green)", ImVec2(colW - 20.0f, 30.0f))) {
                        g_skyboxEnabled = true; g_skyboxR = 0.3f; g_skyboxG = 1.0f; g_skyboxB = 0.6f;
                        g_skyboxIntensity = 0.8f; g_skyboxLerp = 0.7f; g_skyboxLerp2 = 0.5f;
                        QueueCommand(CmdFilterPreset, 4.0f);
                    }
                    ImGui::Spacing();
                    if (ImGui::Button("5. 重置默认原版色彩 (Reset)", ImVec2(colW - 20.0f, 30.0f))) {
                        g_skyboxEnabled = false; g_skyboxR = 1.0f; g_skyboxG = 1.0f; g_skyboxB = 1.0f;
                        g_skyboxIntensity = 1.0f; g_skyboxLerp = 0.0f; g_skyboxLerp2 = 0.0f;
                        QueueCommand(CmdFilterPreset, 0.0f);
                    }
                }
                gui.end_group_box();
            }
            else if (activeSub == 1) {
                // 浪漫飘雪粒子
                gui.group_box(ICON_FA_SNOWFLAKE " 雨爱浪漫飘雪粒子系统 (SnowEffect)", ImVec2(colW, contentH));
                {
                    if (ImGui::Checkbox("启用全屏浪漫落雪", &g_snowEnabled)) {
                        QueueCommand(CmdSnowCount, g_snowEnabled ? g_snowCount : 0.0f);
                    }
                    ImGui::Spacing();

                    if (ImGui::SliderFloat("雪花粒子密度 (Count)", &g_snowCount, 10.0f, 150.0f, "%.0f")) {
                        if (g_snowEnabled) QueueCommand(CmdSnowCount, g_snowCount);
                    }
                    if (ImGui::SliderFloat("飘落速度 (Speed)", &g_snowSpeed, 10.0f, 120.0f, "%.1f")) {
                        QueueCommand(CmdSnowSpeed, g_snowSpeed);
                    }
                    if (ImGui::SliderFloat("雪花平均尺寸 (Size)", &g_snowSize, 2.0f, 12.0f, "%.1f")) {
                        QueueCommand(CmdSnowSize, g_snowSize);
                    }

                    ImGui::Spacing(); ImGui::Separator(); ImGui::Spacing();
                    ImGui::TextDisabled("落雪采用圆圈半透明羽化算法与漂移物理模拟，营造唯美战场意境。");
                }
                gui.end_group_box();

                ImGui::SameLine();

                gui.group_box(ICON_FA_MAGIC " 飘雪状态演示", ImVec2(colW, contentH));
                {
                    ImGui::TextColored(gui.accent_color.to_im_color(), "粒子状态:");
                    ImGui::Text("当前粒子运行状态: %s", g_snowEnabled ? "正在飘落" : "已休眠");
                    ImGui::Text("同屏渲染雪花数: %.0f", g_snowEnabled ? g_snowCount : 0.0f);
                    ImGui::Text("下落速率: %.1f px/s", g_snowSpeed);
                    ImGui::Text("雪花半径: %.1f px", g_snowSize);
                    ImGui::Spacing(); ImGui::Separator(); ImGui::Spacing();
                    ImGui::TextDisabled("支持随时在局内呼出菜单动态调整，无需重进房间。");
                }
                gui.end_group_box();
            }
            else {
                // Unity 原生后处理增强
                gui.group_box(ICON_FA_EYE " Unity PostProcess 画面增强", ImVec2(colW, contentH));
                {
                    if (ImGui::Checkbox("启用画面后处理增强", &g_visualEnabled)) {
                        QueueCommand(CmdVisualToggle, g_visualEnabled ? 1.0f : 0.0f);
                    }
                    ImGui::Spacing();
                    if (ImGui::SliderFloat("画面曝光补偿 (Exposure)", &g_visualExposure, -0.35f, 0.35f, "%.2f")) {
                        QueueCommand(CmdVisualExposure, g_visualExposure);
                    }
                    if (ImGui::SliderFloat("泛光强度 (Bloom)", &g_visualBloom, 0.0f, 1.0f, "%.2f")) {
                        QueueCommand(CmdVisualBloom, g_visualBloom);
                    }
                    if (ImGui::SliderFloat("暗角效果 (Vignette)", &g_visualVignette, 0.0f, 1.0f, "%.2f")) {
                        QueueCommand(CmdVisualVignette, g_visualVignette);
                    }
                }
                gui.end_group_box();

                ImGui::SameLine();

                gui.group_box(ICON_FA_COG " 后处理说明", ImVec2(colW, contentH));
                {
                    ImGui::TextDisabled("后处理调节将实时注入主摄像机 (Camera.main) 的 SSJJPostProcess 模块。");
                    ImGui::Spacing();
                    if (ImGui::Button("重置后处理参数", ImVec2(colW - 20.0f, 32.0f))) {
                        g_visualExposure = 0.0f; g_visualBloom = 0.0f; g_visualVignette = 0.0f;
                        QueueCommand(CmdVisualRestore);
                    }
                }
                gui.end_group_box();
            }
        }
        // ===== TAB 2: COMBAT HUD =====
        else if (gui.m_tab == 2) {
            if (activeSub == 0) {
                gui.group_box(ICON_FA_CROSSHAIRS " CSGO 2 风格全套战斗 HUD (移植自 Vape)", ImVec2(colW, contentH));
                {
                    if (ImGui::Checkbox("启用 CSGO HUD 核心接管", &g_csgoHudEnabled)) {
                        QueueCommand(CmdCsgoHudToggle, g_csgoHudEnabled ? 1.0f : 0.0f);
                    }
                    ImGui::Spacing();
                    if (ImGui::Checkbox("击杀连杀卡片 (Combo A/J/Q/K 动态连击)", &g_csgoHudKillCard)) {
                        QueueCommand(CmdCsgoHudKillCard, g_csgoHudKillCard ? 1.0f : 0.0f);
                    }
                    ImGui::Spacing();
                    if (ImGui::Checkbox("隐藏游戏原生原生 HUD 界面 (纯净模式)", &g_csgoHudSuppress)) {
                        QueueCommand(CmdCsgoHudSuppress, g_csgoHudSuppress ? 1.0f : 0.0f);
                    }
                }
                gui.end_group_box();

                ImGui::SameLine();

                gui.group_box(ICON_FA_DESKTOP " 界面特性说明", ImVec2(colW, contentH));
                {
                    ImGui::BulletText("CS2 经典圆角生命与护甲血量条");
                    ImGui::BulletText("动态击杀播报 (爆头/穿墙/武器图标)");
                    ImGui::BulletText("扑克连击卡牌特效 (Double/Triple/Ace)");
                    ImGui::BulletText("比分与雷达平滑渲染");
                }
                gui.end_group_box();
            }
            else {
                gui.group_box(ICON_FA_MAGIC " 清脆打击音效系统", ImVec2(colW, contentH));
                {
                    if (ImGui::Checkbox("启用击杀打击清脆玻璃音效 (kill_glass.wav)", &g_csgoHudSound)) {
                        QueueCommand(CmdCsgoHudSound, g_csgoHudSound ? 1.0f : 0.0f);
                    }
                    ImGui::Spacing();
                    if (ImGui::SliderFloat("击杀音效音量", &g_csgoHudVolume, 0.0f, 1.0f, "%.2f")) {
                        QueueCommand(CmdCsgoHudVolume, g_csgoHudVolume);
                    }
                }
                gui.end_group_box();

                ImGui::SameLine();

                gui.group_box(ICON_FA_COG " 音效资源存放", ImVec2(colW, contentH));
                {
                    ImGui::TextDisabled("音效音频放置路径:");
                    ImGui::TextColored(gui.accent_color.to_im_color(), "SingleSkinMod/hud/kill_glass.wav");
                    ImGui::Spacing();
                    ImGui::TextDisabled("支持替换为任意标准的 16-bit 44.1kHz WAV 音效文件。");
                }
                gui.end_group_box();
            }
        }
        // ===== TAB 3: SETTINGS =====
        else {
            if (activeSub == 0) {
                gui.group_box(ICON_FA_SLIDERS_H " 玩家几何与属性微调", ImVec2(colW, contentH));
                {
                    std::lock_guard<std::mutex> lock(g_stateMutex);
                    if (ImGui::SliderFloat("人物整体缩放", &g_numeric[0], -5.0f, 5.0f, "%.2f")) {
                        QueueCommand(CmdScale, g_numeric[0]);
                    }
                    if (ImGui::SliderFloat("头部放大倍率", &g_numeric[1], -5.0f, 5.0f, "%.2f")) {
                        QueueCommand(CmdHead, g_numeric[1]);
                    }
                    if (ImGui::SliderFloat("阵营 ID 伪装", &g_numeric[2], 0.0f, 13.0f, "%.0f")) {
                        QueueCommand(CmdTeam, g_numeric[2]);
                    }
                    if (ImGui::SliderFloat("模型不透明度 (Alpha)", &g_numeric[3], 0.0f, 100.0f, "%.0f")) {
                        QueueCommand(CmdAlpha, g_numeric[3]);
                    }
                    if (ImGui::SliderFloat("第一人称不透明度", &g_numeric[4], 0.0f, 100.0f, "%.0f")) {
                        QueueCommand(CmdSelfAlpha, g_numeric[4]);
                    }
                }
                gui.end_group_box();

                ImGui::SameLine();

                gui.group_box(ICON_FA_SHIELD_ALT " 属性应用说明", ImVec2(colW, contentH));
                {
                    ImGui::TextDisabled("调节滑块后，进入对局时本地玩家和视角骨骼矩阵将自动同步。");
                    ImGui::Spacing();
                    if (ImGui::Button("复位默认参数", ImVec2(colW - 20.0f, 32.0f))) {
                        std::lock_guard<std::mutex> lock(g_stateMutex);
                        g_numeric[0] = 1.0f; g_numeric[1] = 1.0f; g_numeric[2] = 0.0f;
                        g_numeric[3] = 100.0f; g_numeric[4] = 100.0f;
                        QueueCommand(CmdScale, 1.0f);
                        QueueCommand(CmdHead, 1.0f);
                        QueueCommand(CmdTeam, 0.0f);
                        QueueCommand(CmdAlpha, 100.0f);
                        QueueCommand(CmdSelfAlpha, 100.0f);
                    }
                }
                gui.end_group_box();
            }
            else {
                gui.group_box(ICON_FA_DESKTOP " 系统连接与状态", ImVec2(colW, contentH));
                {
                    ImGui::Text("TCP 桥接端口: 127.0.0.1:58888");
                    ImGui::TextDisabled("提供全套 HTTP/TCP JSON 接口供外部 WebView 控制台交互。");
                    ImGui::Spacing(); ImGui::Separator(); ImGui::Spacing();
                    ImGui::Text("全局快捷键:");
                    ImGui::BulletText("[Home] 或 [F12] : 开启 / 关闭此菜单");
                    ImGui::BulletText("[F5] : 一键应用所有已选美化");
                    ImGui::BulletText("[F6] : 一键还原官方原始模型");
                }
                gui.end_group_box();

                ImGui::SameLine();

                gui.group_box(ICON_FA_COG " 运行核心信息", ImVec2(colW, contentH));
                {
                    ImGui::Text("核心底层: DirectX 11 DXGI Hook");
                    ImGui::Text("UI 引擎: Neverlose Style Dear ImGui");
                    ImGui::Text("当前帧率: %.1f FPS", ImGui::GetIO().Framerate);
                    ImGui::Spacing(); ImGui::Separator(); ImGui::Spacing();
                    ImGui::TextColored(gui.accent_color.to_im_color(), "Designed for SSJJ Mono x64");
                }
                gui.end_group_box();
            }
        }
    }
    ImGui::EndChild();

    ImGui::PopStyleVar();
    ImGui::End();
}

// ===== Hook Present Implementation =====
static HRESULT __stdcall HookPresent(IDXGISwapChain* pSwapChain, UINT SyncInterval, UINT Flags) {
    if (!g_rendererReady && pSwapChain) {
        DXGI_SWAP_CHAIN_DESC desc;
        pSwapChain->GetDesc(&desc);
        g_window = desc.OutputWindow;
        g_swapChain = pSwapChain;

        if (SUCCEEDED(pSwapChain->GetDevice(IID_PPV_ARGS(&g_d3d11Device)))) {
            g_d3d11Device->GetImmediateContext(&g_d3d11Context);
            CreateRenderTarget(pSwapChain);

            IMGUI_CHECKVERSION();
            ImGui::CreateContext();
            SetupNeverloseStyle();
            LoadNeverloseFonts();

            ImGui_ImplWin32_Init(g_window);
            ImGui_ImplDX11_Init(g_d3d11Device, g_d3d11Context);

            g_originalWndProc = (WNDPROC)SetWindowLongPtrW(g_window, GWLP_WNDPROC, (LONG_PTR)HookWndProc);
            g_rendererReady = true;
        }
    }

    if (g_rendererReady && !g_shuttingDown) {
        if (g_managedTick) {
            try { g_managedTick(); } catch (...) {}
        }

        if (g_menuVisible) {
            ImGui_ImplDX11_NewFrame();
            ImGui_ImplWin32_NewFrame();
            ImGui::NewFrame();

            DrawNeverloseMenu();

            ImGui::Render();
            if (g_renderTargetView) {
                g_d3d11Context->OMSetRenderTargets(1, &g_renderTargetView, nullptr);
                ImGui_ImplDX11_RenderDrawData(ImGui::GetDrawData());
            }
        }
    }

    return g_originalPresent(pSwapChain, SyncInterval, Flags);
}

static HRESULT __stdcall HookPresent1(IDXGISwapChain1* pSwapChain, UINT SyncInterval, UINT Flags, const DXGI_PRESENT_PARAMETERS* pPresentParameters) {
    return HookPresent(pSwapChain, SyncInterval, Flags);
}

static HRESULT __stdcall HookResize(IDXGISwapChain* pSwapChain, UINT BufferCount, UINT Width, UINT Height, DXGI_FORMAT NewFormat, UINT SwapChainFlags) {
    CleanupRenderTarget();
    HRESULT hr = g_originalResize(pSwapChain, BufferCount, Width, Height, NewFormat, SwapChainFlags);
    CreateRenderTarget(pSwapChain);
    return hr;
}

// Dummy window worker thread to extract IDXGISwapChain vtable
static DWORD WINAPI HookWorker(LPVOID) {
    WNDCLASSEXW wc = { sizeof(WNDCLASSEXW), CS_CLASSDC, DefWindowProcW, 0L, 0L, GetModuleHandle(nullptr), nullptr, nullptr, nullptr, nullptr, L"SingleSkinDummy", nullptr };
    RegisterClassExW(&wc);
    HWND wnd = CreateWindowW(wc.lpszClassName, L"Dummy", WS_OVERLAPPEDWINDOW, 0, 0, 100, 100, nullptr, nullptr, wc.hInstance, nullptr);

    const D3D_FEATURE_LEVEL levels[] = { D3D_FEATURE_LEVEL_11_0, D3D_FEATURE_LEVEL_10_1, D3D_FEATURE_LEVEL_10_0 };
    ID3D11Device* device = nullptr;
    ID3D11DeviceContext* context = nullptr;
    D3D_FEATURE_LEVEL level = D3D_FEATURE_LEVEL_11_0;

    HRESULT hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0, levels, 3, D3D11_SDK_VERSION, &device, &level, &context);
    if (FAILED(hr)) {
        hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_WARP, nullptr, 0, levels, 3, D3D11_SDK_VERSION, &device, &level, &context);
    }
    if (FAILED(hr) || !device) {
        DestroyWindow(wnd);
        UnregisterClassW(wc.lpszClassName, wc.hInstance);
        g_hookStarting = false;
        return 0;
    }

    IDXGIDevice* dxgiDevice = nullptr;
    IDXGIAdapter* adapter = nullptr;
    IDXGIFactory2* factory2 = nullptr;
    IDXGISwapChain1* swap1 = nullptr;
    IDXGISwapChain* swap = nullptr;

    if (SUCCEEDED(device->QueryInterface(IID_PPV_ARGS(&dxgiDevice))) &&
        SUCCEEDED(dxgiDevice->GetAdapter(&adapter)) &&
        SUCCEEDED(adapter->GetParent(IID_PPV_ARGS(&factory2)))) {

        DXGI_SWAP_CHAIN_DESC1 desc1{};
        desc1.Width = 2;
        desc1.Height = 2;
        desc1.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
        desc1.SampleDesc.Count = 1;
        desc1.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
        desc1.BufferCount = 2;
        desc1.Scaling = DXGI_SCALING_STRETCH;
        desc1.SwapEffect = DXGI_SWAP_EFFECT_DISCARD;

        hr = factory2->CreateSwapChainForHwnd(device, wnd, &desc1, nullptr, nullptr, &swap1);
        if (FAILED(hr)) {
            DXGI_SWAP_CHAIN_DESC sd{};
            sd.BufferCount = 1;
            sd.BufferDesc.Width = 2;
            sd.BufferDesc.Height = 2;
            sd.BufferDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
            sd.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
            sd.OutputWindow = wnd;
            sd.SampleDesc.Count = 1;
            sd.Windowed = TRUE;
            sd.SwapEffect = DXGI_SWAP_EFFECT_DISCARD;
            IDXGIFactory* factory1 = nullptr;
            if (SUCCEEDED(adapter->GetParent(IID_PPV_ARGS(&factory1)))) {
                factory1->CreateSwapChain(device, &sd, &swap);
                factory1->Release();
            }
        }
    }

    IDXGISwapChain* target = swap1 ? static_cast<IDXGISwapChain*>(swap1) : swap;
    if (target) {
        void** table = *reinterpret_cast<void***>(target);
        if (MH_Initialize() == MH_OK || MH_Initialize() == MH_ERROR_ALREADY_INITIALIZED) {
            if (table[8]) {
                MH_CreateHook(table[8], reinterpret_cast<LPVOID>(HookPresent), reinterpret_cast<void**>(&g_originalPresent));
            }
            if (swap1 && table[22]) {
                MH_CreateHook(table[22], reinterpret_cast<LPVOID>(HookPresent1), reinterpret_cast<void**>(&g_originalPresent1));
            }
            if (table[13]) {
                MH_CreateHook(table[13], reinterpret_cast<LPVOID>(HookResize), reinterpret_cast<void**>(&g_originalResize));
            }
            if (MH_EnableHook(MH_ALL_HOOKS) == MH_OK) {
                g_hookReady = true;
            }
        }
    }

    if (swap1) swap1->Release();
    if (swap) swap->Release();
    if (factory2) factory2->Release();
    if (adapter) adapter->Release();
    if (dxgiDevice) dxgiDevice->Release();
    if (context) context->Release();
    if (device) device->Release();
    DestroyWindow(wnd);
    UnregisterClassW(wc.lpszClassName, wc.hInstance);

    g_hookStarting = false;
    return 0;
}

// ===== C API Exports =====

extern "C" __declspec(dllexport) int __cdecl SSJJUI_Initialize() {
    if (g_hookReady || g_hookStarting.exchange(true)) {
        return 1;
    }
    g_shuttingDown = false;
    HANDLE thread = CreateThread(nullptr, 0, HookWorker, nullptr, 0, nullptr);
    if (!thread) {
        g_hookStarting = false;
        return 0;
    }
    CloseHandle(thread);
    return 1;
}

extern "C" __declspec(dllexport) int __cdecl SSJJUI_IsHookReady() {
    return g_hookReady && g_rendererReady ? 1 : 0;
}

extern "C" __declspec(dllexport) void __cdecl SSJJUI_SetMenuVisible(int visible) {
    g_menuVisible.store(visible != 0);
}

extern "C" __declspec(dllexport) int __cdecl SSJJUI_IsMenuVisible() {
    return g_menuVisible.load() ? 1 : 0;
}

extern "C" __declspec(dllexport) void __cdecl SSJJUI_SetManagedTick(void* fn) {
    g_managedTick = reinterpret_cast<ManagedTickFn>(fn);
}

extern "C" __declspec(dllexport) void __cdecl SSJJUI_Shutdown() {
    g_managedTick = nullptr;
    g_shuttingDown = true;
}

extern "C" __declspec(dllexport) void __cdecl SSJJUI_ResetCatalog(int category) {
    if (category < 0 || category >= 3) return;
    std::lock_guard<std::mutex> lock(g_stateMutex);
    g_catalogs[category].clear();
}

extern "C" __declspec(dllexport) void __cdecl SSJJUI_AddCatalogItem(int category, const wchar_t* item) {
    if (category < 0 || category >= 3 || !item || !*item) return;
    std::lock_guard<std::mutex> lock(g_stateMutex);
    g_catalogs[category].emplace_back(item);
}

extern "C" __declspec(dllexport) void __cdecl SSJJUI_SetSelection(int category, const wchar_t* item) {
    if (category < 0 || category >= 3) return;
    std::lock_guard<std::mutex> lock(g_stateMutex);
    g_selections[category] = item ? item : L"";
}

extern "C" __declspec(dllexport) void __cdecl SSJJUI_SetNumericState(int field, float value) {
    if (field < 0 || field >= 6) return;
    std::lock_guard<std::mutex> lock(g_stateMutex);
    g_numeric[field] = value;
}

extern "C" __declspec(dllexport) void __cdecl SSJJUI_SetStatus(const wchar_t* status) {
    std::lock_guard<std::mutex> lock(g_stateMutex);
    g_status = status ? status : L"";
}

extern "C" __declspec(dllexport) void __cdecl SSJJUI_SetVisualState(int enabled, float exposure, float bloom, float fog, float shafts, float vignette) {
    std::lock_guard<std::mutex> lock(g_stateMutex);
    g_visualEnabled = enabled != 0;
    g_visualExposure = exposure;
    g_visualBloom = bloom;
    g_visualFog = fog;
    g_visualShafts = shafts;
    g_visualVignette = vignette;
}

extern "C" __declspec(dllexport) void __cdecl SSJJUI_SetCsgoHudState(int enabled, int soundEnabled, float volume, int killCard, int suppress) {
    std::lock_guard<std::mutex> lock(g_stateMutex);
    g_csgoHudEnabled = enabled != 0;
    g_csgoHudSound = soundEnabled != 0;
    g_csgoHudVolume = volume;
    g_csgoHudKillCard = killCard != 0;
    g_csgoHudSuppress = suppress != 0;
}

extern "C" __declspec(dllexport) void __cdecl SSJJUI_SetSkyboxState(int enabled, float r, float g, float b, float intensity, float lerp, float lerp2, int snowCount, float snowSpeed, float snowSize, int preset) {
    std::lock_guard<std::mutex> lock(g_stateMutex);
    g_skyboxEnabled = enabled != 0;
    g_skyboxR = r;
    g_skyboxG = g;
    g_skyboxB = b;
    g_skyboxIntensity = intensity;
    g_skyboxLerp = lerp;
    g_skyboxLerp2 = lerp2;
    g_snowEnabled = snowCount > 0;
    g_snowCount = snowCount > 0 ? (float)snowCount : 80.0f;
    g_snowSpeed = snowSpeed;
    g_snowSize = snowSize;
    g_filterPreset = preset;
}

extern "C" __declspec(dllexport) int __cdecl SSJJUI_PollUiCommand(int* outType, float* outVal, wchar_t* outText, int capacity) {
    if (!outType || !outVal || !outText || capacity <= 0) return 0;
    std::lock_guard<std::mutex> lock(g_queueMutex);
    if (g_commandQueue.empty()) return 0;

    UiCommand cmd = g_commandQueue.front();
    g_commandQueue.pop_front();

    *outType = cmd.type;
    *outVal = cmd.value;

    int copyLen = (std::min)((int)cmd.text.length(), capacity - 1);
    wcsncpy_s(outText, capacity, cmd.text.c_str(), copyLen);
    outText[copyLen] = L'\0';

    return 1;
}
'''

target = r'D:\项目\单美化\src\SingleSkinMod.Native\native.cpp'
with open(target, 'w', encoding='utf-8') as f:
    f.write(cpp_code)
print("Successfully written native.cpp!")
