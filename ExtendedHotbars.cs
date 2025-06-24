using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using Newtonsoft.Json;
using UnityEngine.UI;
using UnityEngine.SceneManagement;


namespace ExtendedHotbars
{
	[BepInPlugin("mizuki.ehb", "Extended Hotbars", "1.0.2")]
	public class ExtendedHotbarsPlugin : BaseUnityPlugin
	{
		//Dictionary<int, Hotkeys> hotkeyList = new Dictionary<int, Hotkeys>();

		public static hotkeySaveData saveData = new hotkeySaveData();

		private static string filePath;
		public static bool isDoingLoad = false;

		public static bool isOurHotkey = false;

		public static int lastCharSlot = -1;

		public static ConfigEntry<bool> hideNumbers;
		public static ConfigEntry<int> maxHotkeyBars;
		public static ConfigEntry<KeyboardShortcut> ExtendBarHotkey;

		public static Dictionary<int, List<ConfigEntry<KeyboardShortcut>>> barHotkeys = new Dictionary<int, List<ConfigEntry<KeyboardShortcut>>>();
		public static int maxActiveHotbars = 3;

		public static ConfigEntry<KeyboardShortcut> BarPlusHotkey;
		public static ConfigEntry<KeyboardShortcut> BarMinusHotkey;

		public static int CurrentChar => GameData.CurrentCharacterSlot.index;
		public static int CurrentBarID => saveData.charFlags[CurrentChar].activeBar;
		public static charData CurrentCharData => saveData.charFlags[CurrentChar];
		public static List<hotkeyItem> CurrentBar => saveData.charFlags[CurrentChar].hotkeyList[CurrentBarID];
		public static bool HasBar => saveData.charFlags[CurrentChar].hotkeyList.ContainsKey(CurrentBarID);
		public static bool HasFlag => saveData.charFlags.ContainsKey(CurrentChar);

		public static BepInEx.Logging.ManualLogSource _logger;

		

		public static MethodInfo doHotkeyTaskMethod;

		public static FieldInfo myHKText;
		public static FieldInfo savedStr;
		public static FieldInfo spellSource;

		public static bool isLoaded = false;

		public static bool hasLoadedHotbar = false;

		public void Awake()
		{
			InitConfig();

			Harmony harm = new Harmony("mizuki.ehb");
			MethodInfo orig = AccessTools.Method(typeof(CharSelectManager), "LoadHotkeys");
			MethodInfo patch = AccessTools.Method(typeof(ExtendedHotbarsPlugin), "LoadHotkeys_postPatch");
			harm.Patch(orig, null, new HarmonyMethod(patch));
			
			//pre-patch it as well
			orig = AccessTools.Method(typeof(CharSelectManager), "LoadHotkeys");
			patch = AccessTools.Method(typeof(ExtendedHotbarsPlugin), "LoadHotkeys_prePatch");
			harm.Patch(orig, new HarmonyMethod(patch));

			HarmonyMethod patch2 = new HarmonyMethod(AccessTools.Method(typeof(HotkeysPatches), "AssignSpell_postPatch"));
			orig = AccessTools.Method(typeof(Hotkeys), "AssignSpellFromBook");
			harm.Patch(orig, null, patch2);
			orig = AccessTools.Method(typeof(Hotkeys), "AssignSkillFromBook");
			harm.Patch(orig, null, patch2);
			orig = AccessTools.Method(typeof(Hotkeys), "AssignItemFrominv");
			harm.Patch(orig, null, patch2);
			orig = AccessTools.Method(typeof(Hotkeys), "ClearMe");
			harm.Patch(orig, null, patch2);

			patch2 = new HarmonyMethod(AccessTools.Method(typeof(HotkeysPatches), "DoHotkeyTask_prePatch"));
			orig = AccessTools.Method(typeof(Hotkeys), "DoHotkeyTask");
			harm.Patch(orig, patch2);

			patch2 = new HarmonyMethod(AccessTools.Method(typeof(HotkeysPatches), "HotkeyUpdate_prePatch"));
			patch = AccessTools.Method(typeof(HotkeysPatches), "HotkeyUpdate_postPatch");
			orig = AccessTools.Method(typeof(Hotkeys), "Update");
			harm.Patch(orig, patch2, new HarmonyMethod(patch));

			patch2 = new HarmonyMethod(AccessTools.Method(typeof(HotkeysPatches), "HotkeyStart_postPatch"));
			orig = AccessTools.Method(typeof(Hotkeys), "Start");
			harm.Patch(orig, null, patch2);

			patch2 = new HarmonyMethod(AccessTools.Method(typeof(HotkeysPatches), "EndSpell_prePatch"));
			orig = AccessTools.Method(typeof(SpellVessel), "EndSpell");
			harm.Patch(orig, patch2);

			patch2 = new HarmonyMethod(AccessTools.Method(typeof(HotkeysPatches), "EndSpellNoCD_prePatch"));
			orig = AccessTools.Method(typeof(SpellVessel), "EndSpellNoCD");
			harm.Patch(orig, patch2);

			_logger = Logger;

			var type = typeof(Hotkeys);
			doHotkeyTaskMethod = type.GetMethod("DoHotkeyTask", BindingFlags.NonPublic | BindingFlags.Instance);
			myHKText = type.GetField("MyHKText", BindingFlags.NonPublic | BindingFlags.Instance);
			savedStr = type.GetField("savedStr", BindingFlags.NonPublic | BindingFlags.Instance);
			type = typeof(SpellVessel);
			spellSource = type.GetField("SpellSource", BindingFlags.NonPublic | BindingFlags.Instance);

			//AssignSpellFromBook
			//AssignSkillFromBook
			//AssignItemFrominv

			SceneManager.sceneLoaded += OnSceneLoaded;
		}


		public void InitConfig()
		{
			hideNumbers = Config.Bind<bool>(
				"General",
				"Hide Numbers",
				true,
				"Hides numbers on the hotbars (only extended)"
			);

			maxHotkeyBars = Config.Bind<int>(
				"General",
				"Max Hotbars",
				5,
				"Change the maximum number of hotbars you can swap through"
			);

			ExtendBarHotkey = Config.Bind<KeyboardShortcut>(
				"General",
				"Extend Hotbar Key",
				new KeyboardShortcut(KeyCode.F11),
				"Pressing this key extends the hotbar."
			);

			BarPlusHotkey = Config.Bind<KeyboardShortcut>(
				"General",
				"Next Hotbar Key",
				new KeyboardShortcut(KeyCode.Alpha1, KeyCode.LeftAlt),
				"Pressing this cycles to the next hotbar."
			);

			BarMinusHotkey = Config.Bind<KeyboardShortcut>(
				"General",
				"Previous Hotbar Key",
				new KeyboardShortcut(KeyCode.Alpha2, KeyCode.LeftAlt),
				"Pressing this cycles to the previous hotbar."
			);

			for (int i = 1; i < maxActiveHotbars; i++)
			{
				List<ConfigEntry<KeyboardShortcut>> _shortcuts = new List<ConfigEntry<KeyboardShortcut>>();
				for (int k = 0; k < 10; k++)
				{
					KeyCode numKey = KeyCode.Alpha1 + k;
					if (numKey == KeyCode.Colon)
						numKey = KeyCode.Alpha0;
					
					var hk = Config.Bind<KeyboardShortcut>(
						"Bar " + (i + 1) + " Hotkeys",
						"Hotkey "+(k+1),
						new KeyboardShortcut(numKey, i==1?KeyCode.LeftControl:KeyCode.LeftShift),
						"Activates Skill."
					);
					_shortcuts.Add( hk );
				}

				barHotkeys.Add(i, _shortcuts);
			}

			var dir = Path.Combine(Paths.PluginPath, "ExtendedHotbars");
			filePath = Path.Combine(dir, "hotkeys.json");

			if (File.Exists(filePath))
				saveData = JsonConvert.DeserializeObject<hotkeySaveData>(File.ReadAllText(filePath));

			Logger.LogInfo("[EHB] Loaded.");
		}

		public void Update()
		{
			if(GameData.CurrentCharacterSlot == null) return;
			
			Scene currentScene = SceneManager.GetActiveScene();

			if (currentScene.name == "Menu" || currentScene.name == "LoadScene")
			{
				return;
			}

			if (!saveData.charFlags.ContainsKey(CurrentChar))
				return;

			if (BarPlusHotkey.Value.IsUp())
				barUp();
			if(BarMinusHotkey.Value.IsUp())
				barDown();
			if (ExtendBarHotkey.Value.IsUp())
				OnExtendBar();

		}

		public static void OnExtendBar()
		{
			if (!hasLoadedHotbar)
			{
				GetHotbarObjects();
				return;
			}
			if (!HasFlag) return;

			CurrentCharData.visibleBars++;
			if (CurrentCharData.visibleBars > maxActiveHotbars)
				CurrentCharData.visibleBars = 1;

			LoadAdditionalBars();
		}

		public static void LoadAdditionalBars()
		{
			if (!HasFlag) return;
			Vector3 offset = new Vector3(0, (CurrentCharData.visibleBars - 1) * 30, 0);

			//Move
			foreach (Transform t in easyMoveObjects)
				t.localPosition = _defaultPositions[t.GetInstanceID()] + offset;

			//Move vitals container but don't move xp stuff
			Vector3[] savedPositions = xpObjects.Select(x => x.position).ToArray();

			vitalsContainer.transform.localPosition = _defaultPositions[vitalsContainer.GetInstanceID()] + offset;
			statusBar.localPosition = _defaultPositions[statusBar.GetInstanceID()] + new Vector3(0, (CurrentCharData.visibleBars - 1) * 60, 0);

			for (int i = 0; i < xpObjects.Count; i++)
				xpObjects[i].position = savedPositions[i];

			foreach (var t in additionalHotbars)
			{
				foreach (var f in t.Value.hotkeys)
					Destroy(f.Value.gameObject);
				
				Destroy(t.Value.hotbar.gameObject);
				Destroy(t.Value.background.gameObject);
			}

			additionalHotbars.Clear();
			isDoingLoad = true;
			for (int i = 1; i < CurrentCharData.visibleBars; i++)
			{
				Vector3 barOffset = new Vector3(0, (i) * 60, 0);
				Transform newBar = Instantiate(hotbarBG, hotbarContainer, true);
				Transform hotkeyTransform = Instantiate(hotkeyContainer, hotbarContainer, true);
				//Destroy those pesky children!
				foreach (Transform f in hotkeyTransform)
					Destroy(f.gameObject);

				bool hasBar = saveData.charFlags[CurrentChar].hotkeyList.ContainsKey(i);

				if (!hasBar)
					CreateCustomHotkeyBar(i);

				var barData = saveData.charFlags[CurrentChar].hotkeyList[i];
				var hotKeyData = barHotkeys[i];

				customHotbar hotbar = new customHotbar
				{
					background = newBar,
					hotbar = hotkeyTransform,
					hotkeys = new Dictionary<int, Hotkeys>()
				};
				for (int j = 0; j < 10; j++)
				{
					Transform newHK = Instantiate(hotkey1, hotkeyTransform, true);
					
					//newHK.SetParent(newBar, false);
					var scale = hotkey1.localScale;
					newHK.localScale = scale;
					newHK.localPosition += new Vector3(j * 56, 0, 0);
					var hk = newHK.GetComponent<Hotkeys>();
					hotbar.hotkeys.Add(j, hk);
					hk.ClearMe();

					var hkTxt = hk.GetComponentInChildren<Text>();

					
					int numTxt = j+1;
					if(numTxt == 10)
						numTxt = 0;

					hkTxt.text = numTxt.ToString();
					if (hideNumbers.Value == true)
						hkTxt.text = "";
					TMPro.TMP_Text _buttonText = hk.GetComponentInChildren<TMPro.TMP_Text>();

					ConfigEntry<KeyboardShortcut> hkShortcut = hotKeyData[j];

					//var mainK = hkShortcut.Value.MainKey;
					//var modK = hkShortcut.Value.Modifiers;

					var KLabel = ToShortcutLabel(hkShortcut.Value);

					if (_buttonText != null)
					{
						_buttonText.text = $"{KLabel}";
						var rt = _buttonText.GetComponent<RectTransform>();
						rt.sizeDelta = new Vector2(30, 15);
					}

					//check if we have hotbar for this saved
					if (hasBar)
					{
						var hki = barData[j];
						var skill = hki.name;
						var type = hki.type;
						var idx = hki.index;
						var itm = hki.item;

						switch ((Hotkeys.HKType)type)
						{
							case Hotkeys.HKType.Spell:
								hk.AssignSpellFromBook(GameData.SpellDatabase.GetSpellByID(skill));
								break;
							case Hotkeys.HKType.Skill:
								hk.AssignSkillFromBook(GameData.SkillDatabase.GetSkillByID(skill));
								break;
							case Hotkeys.HKType.Item:
								hk.AssignItemFrominv(GameData.PlayerInv.ALLSLOTS[itm]);
								break;
							default: hk.ClearMe(); break;
						}
					}
				}
				newBar.localPosition += barOffset;
				hotkeyTransform.localPosition += barOffset;
				additionalHotbars.Add(i, hotbar);
			}
			isDoingLoad = false;
		}


		private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			if (scene.name == "LoadScene" || scene.name == "Menu")
			{
				hasLoadedHotbar = false; //We reset the hotbar loading either way
				loadedDefaults = false;
				if(GameData.CurrentCharacterSlot != null) //Save if we get into the menu/char select and have a character
					Save();
				//clear hotbars on disconnect
				foreach (var t in additionalHotbars)
				{
					foreach (var f in t.Value.hotkeys)
						Destroy(f.Value.gameObject);

					Destroy(t.Value.hotbar.gameObject);
					Destroy(t.Value.background.gameObject);
				}

				additionalHotbars.Clear();
			}
			else{
				if (hasLoadedHotbar && GameData.CurrentCharacterSlot != null) //When we get in-game, load the additional bars
					LoadAdditionalBars();
			}
			if (scene.name == "LoadScene")
			{
				GetHotbarObjects(); //Hotbar loads with this scene
			}

		}
		public struct customHotbar
		{
			public Transform background;
			public Transform hotbar;
			public Dictionary<int, Hotkeys> hotkeys;
		}

		private static Dictionary<int, Vector3> _defaultPositions = new Dictionary<int, Vector3>();
		private static bool loadedDefaults = false;
		public static Dictionary<int, customHotbar> additionalHotbars = new Dictionary<int, customHotbar>();

		private static Transform hotbarContainer;
		private static Transform hotkeyContainer;
		private static Transform hotkey1;

		private static Transform vitalsContainer;
		private static List<Transform> xpObjects = new List<Transform>();
		private static List<Transform> easyMoveObjects = new List<Transform>();

		private static Transform hotbarBG;
		private static Transform statusBar;

		public static void GetHotbarObjects()
		{
			if (hasLoadedHotbar) return;

			_logger.LogInfo("[EHB] Tryind to find hotbar.");

			xpObjects.Clear();
			easyMoveObjects.Clear();

			hotbarContainer = GameObject.Find("HotbarPar").transform;
			if (hotbarContainer == null) { _logger.LogError("[EHB] No Hotbar Container."); return; };
			hotkeyContainer = hotbarContainer.Find("Hotkeys");
			if (hotkeyContainer == null) { _logger.LogError("[EHB] No Hotkey Container."); return; };
			//grab hk1 for copying
			hotkey1 = hotkeyContainer.Find("HK1");
			if (hotkey1 == null) { _logger.LogError("[EHB] No HK1."); return; };

			vitalsContainer = hotbarContainer.Find("Vitals");
			if(vitalsContainer == null) { _logger.LogError("[EHB] No Vitals."); return; };
			//xpbg and the text so we can not move it
			Transform xpbg = vitalsContainer.Find("XPBG");
			if (xpbg == null) { _logger.LogError("[EHB] No XPBG."); return; };

			Transform xptxt = vitalsContainer.Find("XPPct");
			if (xptxt == null) { _logger.LogError("[EHB] No XPPct."); return; };

			//get Image (2) (bg of hp)
			Transform hpbg = vitalsContainer.Find("LifeBG");
			if (hpbg == null) { _logger.LogError("[EHB] No HP BG"); return; };
			//bg of SP
			Transform spbg = vitalsContainer.Find("ManaBG");
			if (spbg == null) { _logger.LogError("[EHB] No SP BG."); return; };

			//the flashing thing
			Transform flash = hotbarContainer.GetComponentInChildren<FlashUIColors>().transform;
			if (flash == null) { _logger.LogError("[EHB] No Flash."); return; };

			hotbarBG = hotbarContainer.Find("Image (1)");
			if(hotbarBG == null) { _logger.LogError("[EHB] No Hotbar BG."); return; };

			statusBar = hotbarContainer.Find("StatusSlots");
			if (statusBar == null) { _logger.LogError("[EHB] No Status Slots."); return; };

			//Transform stDrag = hotbarContainer.Find("StatusDrag");
			//if (stDrag == null) { _logger.LogError("[EHB] No Drag."); return; };

			//Transform stToggle = hotbarContainer.Find("Toggle");
			//if (stToggle == null) { _logger.LogError("[EHB] No Toggle."); return; };

			xpObjects.Add(xpbg);
			xpObjects.Add(xptxt);

			easyMoveObjects.Add(hpbg);
			easyMoveObjects.Add(spbg);
			//easyMoveObjects.Add(stSlot);
			//easyMoveObjects.Add(stDrag);
			//easyMoveObjects.Add(stToggle);
			easyMoveObjects.Add(flash);

			hasLoadedHotbar = true;
			_logger.LogInfo("[EHB] Found Hotbar items.");

			if (!loadedDefaults)
			{
				_defaultPositions.Clear();
				_defaultPositions.Add(vitalsContainer.GetInstanceID(), vitalsContainer.localPosition);
				_defaultPositions.Add(statusBar.GetInstanceID(), statusBar.localPosition);
				//_defaultPositions.Add(xpbg.GetInstanceID(), xpbg.position); //Get the global position for these
				//_defaultPositions.Add(xptxt.GetInstanceID(), xptxt.position);
				foreach (Transform t in easyMoveObjects)
					_defaultPositions.Add(t.GetInstanceID(), t.localPosition);
				
				loadedDefaults = true;
				_logger.LogInfo("[EHB] Loaded default positions.");
			}
		}

		

		public static void Save()
		{
			string json = JsonConvert.SerializeObject(saveData, Formatting.Indented);
			File.WriteAllText(filePath, json);

			_logger.LogInfo("[EHB] Saved.");
		}

		public static void barUp()
		{
			int prevBar = CurrentCharData.activeBar;
			CurrentCharData.activeBar++;
			if(CurrentCharData.activeBar > maxHotkeyBars.Value-1)
				CurrentCharData.activeBar = 0;

			UpdateSocialLog.LogAdd($"Hotbar #{CurrentCharData.activeBar + 1}", "green");

			LoadCustomHotkeys(true, prevBar);
		}

		public static void barDown()
		{
			int prevBar = CurrentCharData.activeBar;
			CurrentCharData.activeBar--;
			if (CurrentCharData.activeBar < 0)
				CurrentCharData.activeBar = maxHotkeyBars.Value-1;

			UpdateSocialLog.LogAdd($"Hotbar #{CurrentCharData.activeBar + 1}", "green");

			LoadCustomHotkeys(true, prevBar);
		}


		private void OnDestroy()
		{
			Save();
			SceneManager.sceneLoaded -= OnSceneLoaded;
		}

		
		
		public static void CreateCustomHotkeyBar(int barID)
		{
			List<hotkeyItem> hkList = new List<hotkeyItem>();
			for (int k = 0; k < 10; k++)
			{
				hkList.Add(new hotkeyItem());
				hkList[k].index = k;
			}
			CurrentCharData.hotkeyList.Add(barID, hkList);
			Save();
		}

		public static void LoadCustomHotkeys(bool isSwap=false, int prevBarID = 0)
		{
			isDoingLoad = true;

			if (!saveData.charFlags.ContainsKey(CurrentChar)) return;
			
			if(!HasBar)
				CreateCustomHotkeyBar(CurrentBarID);

			//_logger.LogInfo("[ESS] Loading Bar.");

			//we're swapping bars?
			/*if (isSwap)
			{
				//save cds
				var prevBar = CurrentCharData.hotkeyList[prevBarID];

				int idx = 0;
				foreach (var hk in GameData.GM.HKManager.AllHotkeys)
				{
					string hkSID = hk.AssignedSpell != null ? hk.AssignedSpell.Id : "";
					if (string.IsNullOrEmpty(hkSID))
						hkSID = hk.AssignedSkill != null ? hk.AssignedSkill.Id : "";
					if (string.IsNullOrEmpty(hkSID))
						hkSID = hk.AssignedItem != null ? hk.InvSlotIndex.ToString() : "";

					CurrentCharData.hotkeyCooldowns[hkSID] = hk.Cooldown;
					++idx;
				}
			}*/

			var bar = CurrentBar;
			foreach (var hk in bar)
			{
				var skill = hk.name;
				var type = hk.type;
				var idx = hk.index;
				var itm = hk.item;

				if (string.IsNullOrEmpty(hk.name) && itm == -1) type = 9999; //forces clear

				switch ((Hotkeys.HKType)type)
				{
					case Hotkeys.HKType.Spell:
						GameData.GM.HKManager.AllHotkeys[idx].AssignSpellFromBook(GameData.SpellDatabase.GetSpellByID(skill));
						break;
					case Hotkeys.HKType.Skill:
						GameData.GM.HKManager.AllHotkeys[idx].AssignSkillFromBook(GameData.SkillDatabase.GetSkillByID(skill));
						break;
					case Hotkeys.HKType.Item:
						GameData.GM.HKManager.AllHotkeys[idx].AssignItemFrominv(GameData.PlayerInv.ALLSLOTS[itm]);
						break;
					default: GameData.GM.HKManager.AllHotkeys[idx].ClearMe(); break;
				}
				var hkM = GameData.GM.HKManager.AllHotkeys[idx];

				string hkSID = getHKSkillId(hkM);

				/*if (CurrentCharData.hotkeyCooldowns.ContainsKey(hkSID) && CurrentCharData.hotkeyCooldowns[hkSID] != -1)
					GameData.GM.HKManager.AllHotkeys[idx].Cooldown = CurrentCharData.hotkeyCooldowns[hkSID];
				else
					CurrentCharData.hotkeyCooldowns[hkSID] = -1;*/

			}
			isDoingLoad = false;
		}
		

		public static string getHKSkillId(Hotkeys hk)
		{
			string hkSID = hk.AssignedSpell != null ? hk.AssignedSpell.Id : "";
			if (string.IsNullOrEmpty(hkSID))
				hkSID = hk.AssignedSkill != null ? hk.AssignedSkill.Id : "";
			if (string.IsNullOrEmpty(hkSID))
				hkSID = hk.AssignedItem != null ? hk.InvSlotIndex.ToString() : "";

			return hkSID;
		}
		

		//Loads saved hotkey
		public static void LoadHotkeys_prePatch()
		{
			isDoingLoad = true;
			//_logger.LogInfo("[ESS] LoadHotkeys PRE.");
		}
		public static void LoadHotkeys_postPatch()
		{
			//_logger.LogInfo("[ESS] LoadHotkeys POST.");
			if (!saveData.charFlags.ContainsKey(CurrentChar))
			{
				_logger.LogInfo("[EHB] Found no data.");
				//Create new data for character
				var chData = new charData();
				chData.hotkeyList = new Dictionary<int, List<hotkeyItem>>();
				

				List<hotkeyItem> hkList = new List<hotkeyItem>();

				int idx = 0;
				foreach (var hk in GameData.GM.HKManager.AllHotkeys)
				{
					var item = new hotkeyItem
					{
						type = (int)hk.thisHK,
						name = hk.thisHK == Hotkeys.HKType.Spell ? hk.AssignedSpell?.Id : hk.thisHK == Hotkeys.HKType.Skill ? hk.AssignedSkill?.Id : "",
						index = idx,
						item = hk.thisHK == Hotkeys.HKType.Item ? hk.InvSlotIndex : -1
					};

					hkList.Add(item);
					++idx;
				}
				chData.hotkeyList.Add(0, hkList);
				chData.hasRun = true;

				saveData.charFlags.Add(GameData.CurrentCharacterSlot.index, chData);
			}
			LoadCustomHotkeys();
			isLoaded = true;
		}


		public static string GetKeyLabel(KeyCode key)
		{
			string keyStr = key.ToString();

			if (keyStr.StartsWith("Alpha")) return keyStr.Substring(5); //Alpha1 => 1
			if (keyStr.StartsWith("Keypad")) return "K" + keyStr.Substring(6); //Keypad2 => K2
			if (keyStr.StartsWith("Joystick")) return "J"; //Simplify joystick stuff
			if (keyStr.StartsWith("Mouse")) return "M" + keyStr.Substring(5); //Mouse0 => M0

			switch (key)
			{
				case KeyCode.LeftControl: case KeyCode.RightControl: return "Ctrl";
				case KeyCode.LeftShift: case KeyCode.RightShift: return "Shift";
				case KeyCode.LeftAlt: case KeyCode.RightAlt: return "Alt";
				case KeyCode.Escape: return "Esc";
				case KeyCode.Return: return "Ent";
				case KeyCode.Backspace: return "Bksp";
				case KeyCode.Space: return "Spc";
			}

			//Default fallback (F1, A-Z, etc.)
			return keyStr.Length <= 2 ? keyStr : keyStr.Substring(0, 1).ToUpper();
		}

		public static string ToShortcutLabel(KeyboardShortcut shortcut)
		{
			var parts = new List<string>();

			foreach (var mod in shortcut.Modifiers)
				parts.Add(GetKeyLabel(mod));

			parts.Add(GetKeyLabel(shortcut.MainKey));
			return string.Join("+", parts);
		}
	}


	

	[System.Serializable]
	public class hotkeySaveData
	{
		public Dictionary<int, charData> charFlags = new Dictionary<int, charData>();
	}

	[System.Serializable]
	public class hotkeyItem
	{
		public int type = 9999;
		public string name = "";
		public int index = 0;
		public int item = -1;
	}

	[System.Serializable]
	public class charData
	{
		public int activeBar = 0;
		public bool hasRun = false;
		public int visibleBars = 1;
		public Dictionary<int, List<hotkeyItem>> hotkeyList = new Dictionary<int, List<hotkeyItem>>();

		[NonSerialized]
		public Dictionary<string, float> hotkeyCooldowns = new Dictionary<string, float>();
	}
}
