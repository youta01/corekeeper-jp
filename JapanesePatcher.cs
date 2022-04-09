﻿using System;
using System.Collections.Generic;
using UnhollowerRuntimeLib;
using UnityEngine;
using System.Linq;
using HarmonyLib;
using I2.Loc;
using Il2CppSystem.Net;
using Il2CppSystem.IO;
using Il2CppSystem.Text;
using BepInEx.Configuration;
using System.Reflection;

namespace ckjp
{
	public class JapanesePatcher : MonoBehaviour
	{
		private static ConfigEntry<bool> _isForceJapanese;
		private static ConfigEntry<int> _waitTime;
		private static ConfigEntry<bool> _isIgnoreItemName;

		internal static void Setup()
		{
			_isForceJapanese = BepInExLoader.Inst.Config.Bind("General", "ForceLanguageToJapanese", true, "言語を強制的に日本語にする");
			_waitTime = BepInExLoader.Inst.Config.Bind("General", "PatchJapaneseWaitFrame", 300, "早すぎるとゲーム自体の読み込みと競合するため起動してから指定したフレーム数待機してから日本語を適用する");
			_isIgnoreItemName = BepInExLoader.Inst.Config.Bind("General", "IsIgnoreItemName", false, "アイテム名を日本語化しないようにします。変更後の適用にはゲームの再起動が必要です");
			BepInExLoader.Inst.Log.LogMessage("Japanese Patcher Injected.");
			ClassInjector.RegisterTypeInIl2Cpp<JapanesePatcher>();

			var obj = new GameObject("JapanesePatcher");
			DontDestroyOnLoad(obj);
			obj.hideFlags |= HideFlags.HideAndDontSave;
			obj.AddComponent<JapanesePatcher>();
		}
		private Dictionary<string, string> japaneses;

		internal void Start()
		{
			BepInExLoader.Inst.Log.LogMessage(">>>>>>> Japanese patching... <<<<<<<<<<<");

			var request = WebRequest.Create("https://docs.google.com/spreadsheets/d/1csBM-ZqZtG_z_JdLaFvGHHy8UABZdxRRdT_ShJM5zTE/export?format=tsv");
			request.Method = "Get";
			WebResponse response;
			BepInExLoader.Inst.Log.LogMessage("Downloading...");
			try
			{
				response = request.GetResponse();
			}
			catch
			{
				response = null;
			}

			if (response == null)
				return;

			var st = response.GetResponseStream();
			var sr = new StreamReader(st, Encoding.GetEncoding("UTF-8"));
			string txt = sr.ReadToEnd();
			BepInExLoader.Inst.Log.LogMessage("Downloaded.");

			var rows = txt.Split("\r\n");
			japaneses = new Dictionary<string, string>();
			foreach (var row in rows.Skip(1).Select(row => row.Split("\t")).Where(row => !string.IsNullOrEmpty(row[2])))
			{
				if (row[2][0] != '\'')
					japaneses[row[0]] = row[2];
				else
					japaneses[row[0]] = row[2].Remove(0, 1);
			}

			sr.Close();
			st.Close();

			BepInExLoader.Inst.Log.LogMessage(">>>>>>> Waiting for localization manager <<<<<<<<<<<");
		}

		private int? _detectedFrameCount;

		private static byte[] ReadFully(System.IO.Stream input)
		{
			using var ms = new System.IO.MemoryStream();
			byte[] buffer = new byte[81920];
			int read;
			while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
				ms.Write(buffer, 0, read);
			return ms.ToArray();
		}

		internal void Update()
		{


			if (LocalizationManager.Sources.Count == 0)
				return;

			if (_detectedFrameCount == null)
			{
				BepInExLoader.Inst.Log.LogMessage(">>>>>>> localization manager incoming <<<<<<<<<<<");

				if (!Bootstrapper.Instance.ForcePatching)
				{
					byte[] bytes;
					var texture = new Texture2D(2, 2);
					var asm = Assembly.GetExecutingAssembly();
					
					using (var stream = typeof(BepInExLoader).Assembly.GetManifestResourceStream($"ckjp.Resources.outfile.png"))
						bytes = ReadFully(stream);
					texture.LoadImage(bytes);

					var manager = GameObject.Find("Text Manager").GetComponent<TextManager>();
					JapaneseFontPatch.Patch(manager, texture);
				}

				_detectedFrameCount = Time.frameCount;
				return;
			}

			if (_detectedFrameCount + _waitTime.Value > Time.frameCount)
				return;

			BepInExLoader.Inst.Log.LogMessage(">>>>>>> Wait complete, patching... <<<<<<<<<<<");

			var pred = delegate (I2.Loc.LanguageData x) { return x.Code == "ja"; };
			var jaLang = I2.Loc.LocalizationManager.Sources[0].mLanguages.Find(pred);
			var jaLangIndex = I2.Loc.LocalizationManager.Sources[0].mLanguages.IndexOf(jaLang);

			foreach (var term in I2.Loc.LocalizationManager.Sources[0].mTerms)
			{
				if (term.TermType != I2.Loc.eTermType.Text)
					continue;
				if (!japaneses.ContainsKey(term.Term))
					continue;

				if (string.IsNullOrWhiteSpace(japaneses[term.Term]))
					continue;

				if (_isIgnoreItemName.Value && term.Term.StartsWith("Items/") && !term.Term.EndsWith("Desc"))
					continue;

				term.Languages[jaLangIndex] = japaneses[term.Term];
			}
			
			jaLang.Flags = 0;

			if (_isForceJapanese.Value)
			{
				I2.Loc.LocalizationManager.CurrentLanguage = "japanese";
				var texts = FindObjectsOfType<PugText>();
				foreach (var text in texts)
				{
					if (text.localize)
						text.Render(false);
				}
			}

			BepInExLoader.Inst.Log.LogMessage(">>>>>>> Finished japanese patch. <<<<<<<<<<<");
			Bootstrapper.Instance.ForcePatching = false;
			Destroy(gameObject);
		}
	}
}
