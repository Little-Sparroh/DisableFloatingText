using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System.Reflection;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.InputSystem;
using Pigeon.Movement;

[BepInPlugin(PluginGUID, PluginName, PluginVersion)]
[MycoMod(null, ModFlags.IsClientSide)]
public class DisableFloatingTextMod : BaseUnityPlugin
{
    public const string PluginGUID = "sparroh.disablefloatingtext";
    public const string PluginName = "DisableFloatingText";
    public const string PluginVersion = "1.0.1";

    private ConfigEntry<bool> showEnemyDamageText;
    private ConfigEntry<bool> verboseLogging;
    private bool isEnabled = true;
    private static DisableFloatingTextMod Instance;
    private int updateFrameCounter = 0;
    private FieldInfo poolField;
    private FieldInfo activeTextsField;
    private float lastClearLogTime;
    private int totalCleared = 0;

    private void Awake()
    {
        Instance = this;

        showEnemyDamageText = Config.Bind("General", "ShowEnemyDamageText", true, "Toggle damage text for damage dealt to enemies (true = show, false = hide).");
        verboseLogging = Config.Bind("Debug", "VerboseLogging", false, "Log details on cleared texts (target types, counts) - for troubleshooting.");

        isEnabled = showEnemyDamageText.Value;

        showEnemyDamageText.SettingChanged += OnConfigEntryChanged;

        poolField = typeof(DamageText).GetField("pool", BindingFlags.NonPublic | BindingFlags.Instance);
        activeTextsField = typeof(DamageText).GetField("activeTexts", BindingFlags.NonPublic | BindingFlags.Static);

        var harmony = new Harmony(PluginGUID);
        harmony.PatchAll();
        Logger.LogInfo($"{PluginName} loaded successfully.");
    }

    private void Update()
    {
        if (Keyboard.current.f7Key.wasPressedThisFrame)
        {
            isEnabled = !isEnabled;
            totalCleared = 0;
        }

        updateFrameCounter++;
        if (updateFrameCounter % 60 == 0)
        {
            //Logger.LogInfo("Mod Update() test: Running normally.");
        }
    }

    private void LateUpdate()
    {
        if (!isEnabled && DamageText.activeTexts != null && DamageText.activeTexts.Count > 0)
        {
            int clearedCount = 0;
            string targetTypes = "";

            for (int i = DamageText.activeTexts.Count - 1; i >= 0; i--)
            {
                var dt = DamageText.activeTexts[i];
                if (dt != null && dt.gameObject.activeSelf && dt.Target != null && !(dt.Target is Player))
                {
                    dt.gameObject.SetActive(false);
                    if (poolField != null)
                    {
                        var pool = (ObjectPool<DamageText>)poolField.GetValue(dt);
                        pool?.Release(dt);
                    }
                    clearedCount++;
                    totalCleared++;

                    if (verboseLogging.Value)
                    {
                        string type = dt.Target.GetType().Name;
                        targetTypes += type + ", ";
                    }

                    DamageText.activeTexts.RemoveAt(i);
                }
            }

            if (clearedCount > 0 && (Time.time - lastClearLogTime > 3f || totalCleared == clearedCount))
            {
                string log = $"Runtime clearance: Hidden {clearedCount} enemy texts (total: {totalCleared}).";
                if (verboseLogging.Value && !string.IsNullOrEmpty(targetTypes))
                {
                    log += $" Types: {targetTypes.TrimEnd(',', ' ')}";
                }
                lastClearLogTime = Time.time;
            }
        }
    }

    private void OnConfigEntryChanged(object sender, System.EventArgs e)
    {
        isEnabled = showEnemyDamageText.Value;
    }

    [HarmonyPatch(typeof(Player), "Update")]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.VeryHigh)]
    public static void PrefixPlayerUpdate(Player __instance)
    {
        if (Instance != null && Time.frameCount % 120 == 0)
        {
            //Instance.Logger.LogInfo("Harmony test: Player.Update hit! Patches are working.");
        }
    }

    [HarmonyPatch(typeof(IDamageSource), "DamageTarget", new System.Type[] { typeof(IDamageSource), typeof(ITarget), typeof(DamageData), typeof(Vector3), typeof(IDamageSource) })]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.VeryHigh)]
    public static bool PrefixDamageTarget(IDamageSource source, ITarget target, DamageData damage, Vector3 hitPoint, IDamageSource childSource)
    {
        if (Instance == null) return true;

        string targetType = target?.GetType().Name ?? "Unknown";
        bool isPlayer = (target is Player);

        return true;
    }

    [HarmonyPatch(typeof(IDamageSource), "DamageTarget", new System.Type[] { typeof(IDamageSource), typeof(ITarget), typeof(DamageData), typeof(Vector3), typeof(IDamageSource) })]
    [HarmonyPostfix]
    [HarmonyPriority(Priority.VeryHigh)]
    public static void PostfixDamageTarget(IDamageSource source, ITarget target, DamageData damage, Vector3 hitPoint, IDamageSource childSource)
    {
        if (Instance != null && !Instance.isEnabled && target != null && !(target is Player))
        {
            Instance.LateUpdate();
        }
    }
}