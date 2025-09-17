using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System.Reflection;
using UnityEngine;
using UnityEngine.Pool; // For ObjectPool<T>
using UnityEngine.InputSystem; // For new Input System (F7 toggle)
using Unity.Netcode; // For NetworkBehaviour and ClientRpcParams
using Pigeon.Movement; // For Player and ITarget

[BepInPlugin("com.yourname.disablefloatingtext", "DisableFloatingText", "1.0.0")]
[MycoMod(null, ModFlags.IsClientSide)]
public class DisableFloatingTextMod : BaseUnityPlugin
{
    private ConfigEntry<bool> showEnemyDamageText;
    private ConfigEntry<bool> verboseLogging; // New: Toggle detailed logs
    private bool isEnabled = true; // Runtime toggle state (true = show enemy damage text)
    private static DisableFloatingTextMod Instance; // Static instance for Harmony access
    private int updateFrameCounter = 0; // For test logging
    private FieldInfo poolField; // Cached reflection for private pool
    private FieldInfo activeTextsField; // For list manipulation if needed
    private float lastClearLogTime; // For non-spammy logging
    private int totalCleared = 0; // Cumulative count for logs

    private void Awake()
    {
        // Set instance immediately
        Instance = this;

        // Config setup
        showEnemyDamageText = Config.Bind("General", "ShowEnemyDamageText", true, "Toggle damage text for damage dealt to enemies (true = show, false = hide).");
        verboseLogging = Config.Bind("Debug", "VerboseLogging", false, "Log details on cleared texts (target types, counts) - for troubleshooting.");

        // Runtime toggle starts as config value
        isEnabled = showEnemyDamageText.Value;

        // Hook config changes
        showEnemyDamageText.SettingChanged += OnConfigEntryChanged;

        // Cache reflections
        poolField = typeof(DamageText).GetField("pool", BindingFlags.NonPublic | BindingFlags.Instance);
        activeTextsField = typeof(DamageText).GetField("activeTexts", BindingFlags.NonPublic | BindingFlags.Static); // If needed for direct manip
        Logger.LogInfo($"Reflection: poolField={poolField != null}, activeTextsField={activeTextsField != null}");

        // Harmony for patching (kept as backup)
        var harmony = new Harmony("com.yourname.disablefloatingtext");
        harmony.PatchAll();
        Logger.LogInfo($"Harmony created with ID: {harmony.Id}. PatchAll() called. Runtime clearance active for full coverage.");

        Logger.LogInfo("DisableFloatingTextMod loaded! Press F7 to toggle enemy damage text in-game. Runtime clearance enhanced for 100% hide.");
    }

    // F7 toggle handler (in Update)
    private void Update()
    {
        if (Keyboard.current.f7Key.wasPressedThisFrame)
        {
            isEnabled = !isEnabled;
            totalCleared = 0; // Reset counter on toggle
            //Logger.LogInfo($"Enemy Damage Text {(isEnabled ? "ENABLED" : "DISABLED")}.");
        }

        // Test: Log every 60 frames to confirm Update runs
        updateFrameCounter++;
        if (updateFrameCounter % 60 == 0)
        {
            //Logger.LogInfo("Mod Update() test: Running normally.");
        }
    }

    // LateUpdate for post-spawn clearance (catches mid-frame spawns)
    private void LateUpdate()
    {
        // Enhanced Runtime fallback - Clear ALL non-player damage texts every frame if disabled
        if (!isEnabled && DamageText.activeTexts != null && DamageText.activeTexts.Count > 0)
        {
            int clearedCount = 0;
            string targetTypes = ""; // For verbose log

            // Loop backward to avoid index shifts on deactivate
            for (int i = DamageText.activeTexts.Count - 1; i >= 0; i--)
            {
                var dt = DamageText.activeTexts[i];
                if (dt != null && dt.gameObject.activeSelf && dt.Target != null && !(dt.Target is Player))
                {
                    dt.gameObject.SetActive(false);
                    // Clean up pool via reflection
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
                        targetTypes += type + ", "; // Collect types
                    }

                    // Optional: Remove from list immediately (if safe)
                    DamageText.activeTexts.RemoveAt(i);
                }
            }

            // Log every 3s or on first clear (non-spammy)
            if (clearedCount > 0 && (Time.time - lastClearLogTime > 3f || totalCleared == clearedCount))
            {
                string log = $"Runtime clearance: Hidden {clearedCount} enemy texts (total: {totalCleared}).";
                if (verboseLogging.Value && !string.IsNullOrEmpty(targetTypes))
                {
                    log += $" Types: {targetTypes.TrimEnd(',', ' ')}";
                }
                Logger.LogInfo(log);
                lastClearLogTime = Time.time;
            }
        }
    }

    // Config change handler
    private void OnConfigEntryChanged(object sender, System.EventArgs e)
    {
        isEnabled = showEnemyDamageText.Value;
        //Logger.LogInfo($"Enemy Damage Text config updated to: {(isEnabled ? "ENABLED" : "DISABLED")}.");
    }

    // Backup patches (with high priority for Mono inlining) - optional, can remove if runtime works
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

    // ... (Keep other patches as-is for fallback; they're not essential now)
    // Prefix for IDamageSource.DamageTarget (high priority)
    [HarmonyPatch(typeof(IDamageSource), "DamageTarget", new System.Type[] { typeof(IDamageSource), typeof(ITarget), typeof(DamageData), typeof(Vector3), typeof(IDamageSource) })]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.VeryHigh)]
    public static bool PrefixDamageTarget(IDamageSource source, ITarget target, DamageData damage, Vector3 hitPoint, IDamageSource childSource)
    {
        if (Instance == null) return true;

        string targetType = target?.GetType().Name ?? "Unknown";
        bool isPlayer = (target is Player);
        Instance.Logger.LogInfo($"IDamageSource.DamageTarget called - Target: {targetType}, IsPlayer: {isPlayer}, Enabled: {Instance.isEnabled}, Damage: {damage.damage}");

        return true;
    }

    // Postfix for IDamageSource.DamageTarget (triggers immediate clear if needed)
    [HarmonyPatch(typeof(IDamageSource), "DamageTarget", new System.Type[] { typeof(IDamageSource), typeof(ITarget), typeof(DamageData), typeof(Vector3), typeof(IDamageSource) })]
    [HarmonyPostfix]
    [HarmonyPriority(Priority.VeryHigh)]
    public static void PostfixDamageTarget(IDamageSource source, ITarget target, DamageData damage, Vector3 hitPoint, IDamageSource childSource)
    {
        if (Instance != null && !Instance.isEnabled && target != null && !(target is Player))
        {
            // Trigger immediate hide (sync with LateUpdate)
            Instance.LateUpdate(); // Force a clear on this frame
            //Instance.Logger.LogInfo("DamageTarget postfix: Triggered immediate enemy text clear.");
        }
    }

    // (Omit other patches for brevity; add back if needed from v1.0.8)
}