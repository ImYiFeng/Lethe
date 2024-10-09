﻿using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using BepInEx;
using BepInEx.Logging;
using Il2CppSystem.Collections.Generic;
using SimpleJSON;
using UnhollowerRuntimeLib;
using UnityEngine;
using Object = Il2CppSystem.Object;

namespace CustomEncounter;

public class CustomEncounterHook : MonoBehaviour
{
    public static ManualLogSource LOG;
    public static StageStaticData Encounter;

    public static DirectoryInfo CustomAppearanceDir, CustomSpriteDir, CustomLocaleDir, CustomAssistantDir;
    private static string _tokenPath;
    private static HttpListener _listener;

    private static readonly List<Object> _gcPrevent = new();

    public CustomEncounterHook(IntPtr ptr) : base(ptr)
    {
    }

    internal void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha0))
        {
            EncounterHelper.SaveLocale();
            EncounterHelper.SaveEncounters();
            EncounterHelper.SaveIdentities();
        }

        if (Input.GetKeyDown(KeyCode.Alpha9))
        {
            LOG.LogInfo("Entering custom fight");
            try
            {
                var json = File.ReadAllText(CustomEncounterMod.EncounterConfig);
                LOG.LogInfo("Fight data:\n" + json);
                Encounter = JsonUtility.FromJson<StageStaticData>(json);
                LOG.LogInfo("Success, please go to excavation 1 to start the fight.");
            }
            catch (Exception ex)
            {
                LOG.LogError("Error loading custom fight: " + ex.Message);
            }
        }
    }

    public static string AccountJwt()
    {
        return File.Exists(_tokenPath) ? File.ReadAllText(_tokenPath) : "";
    }

    internal static void Setup(ManualLogSource log)
    {
        ClassInjector.RegisterTypeInIl2Cpp<CustomEncounterHook>();

        LOG = log;

        GameObject obj = new("CustomEncounterHook");
        DontDestroyOnLoad(obj);
        obj.hideFlags |= HideFlags.HideAndDontSave;
        var hook = obj.AddComponent<CustomEncounterHook>();
        _gcPrevent.Add(hook);

        CustomAppearanceDir = Directory.CreateDirectory(Path.Combine(Paths.ConfigPath, "custom_appearance"));
        CustomSpriteDir = Directory.CreateDirectory(Path.Combine(Paths.ConfigPath, "custom_sprites"));
        CustomLocaleDir = Directory.CreateDirectory(Path.Combine(Paths.ConfigPath, "custom_limbus_locale"));
        CustomAssistantDir = Directory.CreateDirectory(Path.Combine(Paths.ConfigPath, "custom_assistant"));

        var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _tokenPath = Path.Combine(appdata, "LimbusPrivateServer.jwt");

        _listener = new HttpListener();

        var thread = new Thread(HttpCoroutine);
        thread.Start();
    }

    public static void StopHttp()
    {
        _listener.Stop();
    }

    private static void HttpCoroutine()
    {
        _listener.Prefixes.Add("http://localhost:49829/");

        try
        {
            LOG.LogInfo("Starting HTTP server at 49829...");
            _listener.Start();
            LOG.LogInfo("Started HTTP server at 49829...");
            ServerLoop();
        }
        catch (Exception ex)
        {
            LOG.LogError("Failed to start HTTP server at 49829: " + ex.Message);
        }
    }

    private static void ServerLoop()
    {
        while (_listener.IsListening)
        {
            var ctx = _listener.GetContext();
            LOG.LogInfo($"Request: {ctx.Request.HttpMethod} {ctx.Request.Url.LocalPath}");
            var req = ctx.Request;
            using var resp = ctx.Response;
            resp.StatusCode = (int)HttpStatusCode.OK;

            resp.Headers.Add("Content-Type", "application/json");
            resp.Headers.Add("Access-Control-Allow-Methods", "*");
            resp.Headers.Add("Access-Control-Allow-Headers", "*");
            resp.Headers.Add("Vary", "Origin");
            var origin = req.Headers.Get("Origin");
            if (origin != null && origin.StartsWith("http://localhost:"))
                resp.Headers.Add("Access-Control-Allow-Origin", origin);
            else
                resp.Headers.Add("Access-Control-Allow-Origin", "https://limbus.windtfw.com");

            if (req.HttpMethod == "OPTIONS") continue;

            try
            {
                switch (req.Url.LocalPath)
                {
                    case "/auth/login":
                        AuthLogin(req, resp);
                        break;
                    default:
                        resp.StatusCode = 404;
                        break;
                }
            }
            catch
            {
                resp.StatusCode = 500;
            }
        }
    }

    private static void AuthLogin(HttpListenerRequest req, HttpListenerResponse resp)
    {
        if (req.HttpMethod != "POST")
        {
            resp.StatusCode = 405;
            return;
        }

        var buf = new byte[8192];
        var read = req.InputStream.Read(buf, 0, buf.Length);
        var json = Encoding.UTF8.GetString(buf, 0, read);

        var token = JSON.Parse(json)["token"].Value;
        System.IO.File.WriteAllText(_tokenPath, token);
    }
}