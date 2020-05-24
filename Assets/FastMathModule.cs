using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using KModkit;
using Newtonsoft.Json;
using UnityEngine;

using Rnd = UnityEngine.Random;

public class FastMathModule : MonoBehaviour
{
    public class ModSettingsJSON
    {
        public int countdownTime;
        public string note;
    }

    public KMAudio Audio;
    public KMBombModule Module;
    public KMBombInfo Info;
    public KMRuleSeedable RuleSeedable;
    public KMModSettings modSettings;
    public KMSelectable[] btn;
    public KMSelectable go, submit;
    public TextMesh Screen, goText;
    public GameObject barControl;
    public MeshRenderer bar, goBtn;

    // Rules
    private static readonly int[,] _seed1_numberField = new int[13, 13] {
        { 25, 11, 53, 97, 02, 42, 51, 97, 12, 86, 55, 73, 33 },
        { 54, 07, 32, 19, 84, 33, 27, 78, 26, 46, 09, 13, 58 },
        { 86, 37, 44, 01, 05, 26, 93, 49, 18, 69, 23, 40, 22 },
        { 54, 28, 77, 93, 11, 00, 35, 61, 27, 48, 13, 72, 80 },
        { 99, 36, 23, 95, 67, 05, 26, 17, 44, 60, 26, 41, 67 },
        { 74, 95, 03, 04, 56, 23, 54, 29, 52, 38, 10, 76, 98 },
        { 88, 46, 37, 96, 02, 52, 81, 37, 12, 70, 14, 36, 78 },
        { 54, 43, 12, 65, 94, 03, 47, 23, 16, 62, 73, 46, 21 },
        { 07, 33, 26, 01, 67, 26, 27, 77, 83, 14, 27, 93, 09 },
        { 63, 64, 94, 27, 48, 84, 33, 10, 16, 74, 43, 99, 04 },
        { 35, 39, 03, 25, 47, 62, 38, 45, 88, 48, 34, 31, 27 },
        { 67, 30, 27, 71, 09, 11, 44, 37, 18, 40, 32, 15, 78 },
        { 13, 23, 26, 85, 92, 12, 73, 56, 81, 07, 75, 47, 99 }
    };
    private static readonly string _seed1_letters = "ABCDEGKNPSTXZ";

    private static int[,] numberField;
    private static string letters;
    private static int offset;

    // Module execution
    private bool _isSolved = false, _lightsOn = false, _pressedGo = false;
    private int numStages, stage = 1, input = 0, digitsEntered = 0, threshold = 10, answer;
    private static int _moduleIdCounter = 1;
    private int _moduleId = 0;

    struct RuleInfo
    {
        public string Name;
        public bool Applies;
        public Func<MonoRandom, RuleInfo> Generator;
        public int Offset;
    }

    void Start()
    {
        _moduleId = _moduleIdCounter++;

        go.OnInteract += delegate ()
        {
            GoBtnHandle();
            return false;
        };
        submit.OnInteract += delegate ()
        {
            AnsChk();
            return false;
        };
        for (int i = 0; i < 10; i++)
        {
            int j = i;
            btn[i].OnInteract += delegate ()
            {
                HandlePress(j);
                return false;
            };
        }

        // RULE SEED
        var rnd = RuleSeedable.GetRNG();
        if (rnd.Seed == 1)
        {
            numberField = _seed1_numberField;
            letters = _seed1_letters;
            offset = 0;
            Debug.LogFormat("[Fast Math #{0}] Calculating offset:", _moduleId);
            if (Info.IsIndicatorOn(Indicator.MSA))
            {
                offset += 20;
                Debug.LogFormat("[Fast Math #{0}] Lit MSA: +20 => {1}", _moduleId, offset);
            }
            if (Info.IsPortPresent(Port.Serial))
            {
                offset += 14;
                Debug.LogFormat("[Fast Math #{0}] Serial port: +14 => {1}", _moduleId, offset);
            }
            if (Info.GetSerialNumberLetters().Any("FAST".Contains))
            {
                offset -= 5;
                Debug.LogFormat("[Fast Math #{0}] S/N contains FAST: -5 => {1}", _moduleId, offset);
            }
            if (Info.IsPortPresent(Port.RJ45))
            {
                offset += 27;
                Debug.LogFormat("[Fast Math #{0}] RJ-45 port: +27 => {1}", _moduleId, offset);
            }
            if (Info.GetBatteryCount() > 3)
            {
                offset -= 15;
                Debug.LogFormat("[Fast Math #{0}] More than 3 batt: -15 => {1}", _moduleId, offset);
            }
        }
        else
        {
            numberField = new int[13, 13];

            var candidates = new Dictionary<char, List<RuleInfo>>();
            candidates['s'] = new List<RuleInfo>();
            candidates['s'].Add(new RuleInfo { Name = "the last digit of the serial number is even", Applies = Info.GetSerialNumberNumbers().Last() % 2 == 0 });
            candidates['s'].Add(new RuleInfo { Name = "the serial number contains a vowel", Applies = Info.GetSerialNumberLetters().Any(ch => "AEIOU".Contains(ch)) });
            candidates['s'].Add(new RuleInfo
            {
                Generator = r =>
                {
                    var values = r.ShuffleFisherYates(Enumerable.Range(0, 36).ToArray()).Take(4).ToArray();
                    var chs = values.Select(v => v < 10 ? (char) ('0' + v) : (char) ('A' + v - 10)).ToArray();
                    return new RuleInfo { Name = string.Format(@"the serial number contains any of {0}", chs.Join("")), Applies = Info.GetSerialNumber().Any(ch => chs.Contains(ch)) };
                }
            });

            candidates['p'] = new List<RuleInfo>();
            candidates['p'].Add(new RuleInfo { Name = "the bomb has a parallel port", Applies = Info.IsPortPresent(Port.Parallel) });
            candidates['p'].Add(new RuleInfo { Name = "the bomb has a serial port", Applies = Info.IsPortPresent(Port.Serial) });
            candidates['p'].Add(new RuleInfo { Name = "the bomb has a PS/2 port", Applies = Info.IsPortPresent(Port.PS2) });
            candidates['p'].Add(new RuleInfo { Name = "the bomb has a Stereo RCA port", Applies = Info.IsPortPresent(Port.StereoRCA) });
            candidates['p'].Add(new RuleInfo { Name = "the bomb has a RJ-45 port", Applies = Info.IsPortPresent(Port.RJ45) });
            candidates['p'].Add(new RuleInfo { Name = "the bomb has a DVI-D port", Applies = Info.IsPortPresent(Port.DVI) });
            candidates['p'].Add(new RuleInfo { Name = "the bomb has a duplicate port", Applies = Info.IsDuplicatePortPresent() });
            candidates['p'].Add(new RuleInfo { Name = "the bomb has an empty port plate", Applies = Info.GetPortPlates().Any(p => p.Length == 0) });

            candidates['i'] = new List<RuleInfo>();
            candidates['i'].Add(new RuleInfo { Name = "the bomb has an SND indicator", Applies = Info.IsIndicatorPresent(Indicator.SND) });
            candidates['i'].Add(new RuleInfo { Name = "the bomb has a CLR indicator", Applies = Info.IsIndicatorPresent(Indicator.CLR) });
            candidates['i'].Add(new RuleInfo { Name = "the bomb has a CAR indicator", Applies = Info.IsIndicatorPresent(Indicator.CAR) });
            candidates['i'].Add(new RuleInfo { Name = "the bomb has an IND indicator", Applies = Info.IsIndicatorPresent(Indicator.IND) });
            candidates['i'].Add(new RuleInfo { Name = "the bomb has an FRQ indicator", Applies = Info.IsIndicatorPresent(Indicator.FRQ) });
            candidates['i'].Add(new RuleInfo { Name = "the bomb has an SIG indicator", Applies = Info.IsIndicatorPresent(Indicator.SIG) });
            candidates['i'].Add(new RuleInfo { Name = "the bomb has an NSA indicator", Applies = Info.IsIndicatorPresent(Indicator.NSA) });
            candidates['i'].Add(new RuleInfo { Name = "the bomb has an MSA indicator", Applies = Info.IsIndicatorPresent(Indicator.MSA) });
            candidates['i'].Add(new RuleInfo { Name = "the bomb has a TRN indicator", Applies = Info.IsIndicatorPresent(Indicator.TRN) });
            candidates['i'].Add(new RuleInfo { Name = "the bomb has a BOB indicator", Applies = Info.IsIndicatorPresent(Indicator.BOB) });
            candidates['i'].Add(new RuleInfo { Name = "the bomb has an FRK indicator", Applies = Info.IsIndicatorPresent(Indicator.FRK) });
            candidates['i'].Add(new RuleInfo { Name = "the bomb has a lit SND indicator", Applies = Info.IsIndicatorOn(Indicator.SND) });
            candidates['i'].Add(new RuleInfo { Name = "the bomb has a lit CLR indicator", Applies = Info.IsIndicatorOn(Indicator.CLR) });
            candidates['i'].Add(new RuleInfo { Name = "the bomb has a lit CAR indicator", Applies = Info.IsIndicatorOn(Indicator.CAR) });
            candidates['i'].Add(new RuleInfo { Name = "the bomb has a lit IND indicator", Applies = Info.IsIndicatorOn(Indicator.IND) });
            candidates['i'].Add(new RuleInfo { Name = "the bomb has a lit FRQ indicator", Applies = Info.IsIndicatorOn(Indicator.FRQ) });
            candidates['i'].Add(new RuleInfo { Name = "the bomb has a lit SIG indicator", Applies = Info.IsIndicatorOn(Indicator.SIG) });
            candidates['i'].Add(new RuleInfo { Name = "the bomb has a lit NSA indicator", Applies = Info.IsIndicatorOn(Indicator.NSA) });
            candidates['i'].Add(new RuleInfo { Name = "the bomb has a lit MSA indicator", Applies = Info.IsIndicatorOn(Indicator.MSA) });
            candidates['i'].Add(new RuleInfo { Name = "the bomb has a lit TRN indicator", Applies = Info.IsIndicatorOn(Indicator.TRN) });
            candidates['i'].Add(new RuleInfo { Name = "the bomb has a lit BOB indicator", Applies = Info.IsIndicatorOn(Indicator.BOB) });
            candidates['i'].Add(new RuleInfo { Name = "the bomb has a lit FRK indicator", Applies = Info.IsIndicatorOn(Indicator.FRK) });
            candidates['i'].Add(new RuleInfo { Name = "the bomb has an unlit SND indicator", Applies = Info.IsIndicatorOff(Indicator.SND) });
            candidates['i'].Add(new RuleInfo { Name = "the bomb has an unlit CLR indicator", Applies = Info.IsIndicatorOff(Indicator.CLR) });
            candidates['i'].Add(new RuleInfo { Name = "the bomb has an unlit CAR indicator", Applies = Info.IsIndicatorOff(Indicator.CAR) });
            candidates['i'].Add(new RuleInfo { Name = "the bomb has an unlit IND indicator", Applies = Info.IsIndicatorOff(Indicator.IND) });
            candidates['i'].Add(new RuleInfo { Name = "the bomb has an unlit FRQ indicator", Applies = Info.IsIndicatorOff(Indicator.FRQ) });
            candidates['i'].Add(new RuleInfo { Name = "the bomb has an unlit SIG indicator", Applies = Info.IsIndicatorOff(Indicator.SIG) });
            candidates['i'].Add(new RuleInfo { Name = "the bomb has an unlit NSA indicator", Applies = Info.IsIndicatorOff(Indicator.NSA) });
            candidates['i'].Add(new RuleInfo { Name = "the bomb has an unlit MSA indicator", Applies = Info.IsIndicatorOff(Indicator.MSA) });
            candidates['i'].Add(new RuleInfo { Name = "the bomb has an unlit TRN indicator", Applies = Info.IsIndicatorOff(Indicator.TRN) });
            candidates['i'].Add(new RuleInfo { Name = "the bomb has an unlit BOB indicator", Applies = Info.IsIndicatorOff(Indicator.BOB) });
            candidates['i'].Add(new RuleInfo { Name = "the bomb has an unlit FRK indicator", Applies = Info.IsIndicatorOff(Indicator.FRK) });

            candidates['b'] = new List<RuleInfo>();
            candidates['b'].Add(new RuleInfo { Generator = r => { var n = rnd.Next(2, 5); return new RuleInfo { Name = string.Format("the bomb has more than {0} batteries", n), Applies = Info.GetBatteryCount() > n }; } });
            candidates['b'].Add(new RuleInfo { Generator = r => { var n = rnd.Next(2, 5); return new RuleInfo { Name = string.Format("the bomb has fewer than {0} batteries", n), Applies = Info.GetBatteryCount() < n }; } });
            candidates['b'].Add(new RuleInfo { Generator = r => { var n = rnd.Next(1, 3); var n2 = rnd.Next(n + 1, n + 3); return new RuleInfo { Name = string.Format("the bomb has between {0} and {1} batteries", n, n2), Applies = Info.GetBatteryCount() >= n && Info.GetBatteryCount() <= n2 }; } });
            candidates['b'].Add(new RuleInfo { Generator = r => { var n = rnd.Next(2, 5); return new RuleInfo { Name = string.Format("the bomb has more than {0} battery holders", n), Applies = Info.GetBatteryHolderCount() > n }; } });
            candidates['b'].Add(new RuleInfo { Generator = r => { var n = rnd.Next(2, 5); return new RuleInfo { Name = string.Format("the bomb has fewer than {0} battery holders", n), Applies = Info.GetBatteryHolderCount() < n }; } });
            candidates['b'].Add(new RuleInfo { Generator = r => { var n = rnd.Next(1, 3); var n2 = rnd.Next(n + 1, n + 3); return new RuleInfo { Name = string.Format("the bomb has between {0} and {1} battery holders", n, n2), Applies = Info.GetBatteryHolderCount() >= n && Info.GetBatteryHolderCount() <= n2 }; } });

            var rules = new RuleInfo[5];
            var offsets = rnd.ShuffleFisherYates(Enumerable.Range(0, 60).Select(i => i < 30 ? i - 30 : i - 29).ToArray());
            var ruleTypes = new List<char> { 's', 'p', 'i', 'b' };
            ruleTypes.Add(new[] { 's', 'p', 'i', 'b' }[rnd.Next(0, 4)]);
            for (var i = 0; i < ruleTypes.Count; i++)
            {
                var cand = candidates[ruleTypes[i]];
                var ix = rnd.Next(0, cand.Count);
                rules[i] = cand[ix];
                cand.RemoveAt(ix);
            }
            rnd.ShuffleFisherYates(rules);

            for (var i = 0; i < rules.Length; i++)
            {
                if (rules[i].Generator != null)
                    rules[i] = rules[i].Generator(rnd);
                rules[i].Offset = offsets[i];
            }

            var swapped = rnd.Next(0, 2) != 0;
            letters = rnd.ShuffleFisherYates(new[] { 'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'J', 'K', 'L', 'M', 'N', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z' }).Take(13).OrderBy(c => c).Join("");
            var numbers = rnd.ShuffleFisherYates(Enumerable.Range(0, 200).Select(i => i % 100).ToArray());

            for (var i = 0; i < 13; i++)
                for (var j = 0; j < 13; j++)
                    if (swapped)
                        numberField[j, i] = numbers[i * 13 + j];
                    else
                        numberField[i, j] = numbers[i * 13 + j];

            offset = 0;
            Debug.LogFormat("[Fast Math #{0}] Calculating offset:", _moduleId);
            foreach (var rule in rules)
                if (rule.Applies)
                {
                    offset += rule.Offset;
                    Debug.LogFormat("[Fast Math #{0}] {1}: {2}{3} => {4}", _moduleId, rule.Name, rule.Offset > 0 ? "+" : "", rule.Offset, offset);
                }
        }

        Module.OnActivate += Activate;
    }

    void Activate()
    {
        Init();
        _lightsOn = true;
    }

    void Init()
    {
        numStages = Rnd.Range(3, 6);
        Debug.LogFormat("[Fast Math #{0}] This module will have {1} stages.", _moduleId, numStages);
        threshold = FindThreshold();
        Debug.LogFormat("[Fast Math #{0}] Threshold time set to {1} seconds.", _moduleId, threshold);
        GenerateStage(1);

        _pressedGo = false;
        stage = 1;
        input = 0;
        digitsEntered = 0;
        goBtn.material.color = new Color32(229, 57, 53, 255);
        submit.GetComponent<MeshRenderer>().material.color = Color.gray;
        for (int i = 0; i < 10; i++)
            btn[i].GetComponent<MeshRenderer>().material.color = Color.gray;
    }

    void GenerateStage(int num)
    {
        int randLeft = Rnd.Range(0, 13), randRight = Rnd.Range(0, 13);

        Debug.LogFormat("[Fast Math #{0}] <Stage {1}> Start!", _moduleId, num);
        Debug.LogFormat("[Fast Math #{0}] <Stage {1}> Characters are {2}{3}", _moduleId, num, letters[randLeft], letters[randRight]);

        Screen.text = letters[randLeft] + " " + letters[randRight];

        answer = numberField[randLeft, randRight];
        Debug.LogFormat("[Fast Math #{0}] <Stage {1}> Number in the table is {2}", _moduleId, num, answer);
        answer += offset;
        Debug.LogFormat("[Fast Math #{0}] <Stage {1}> Adding offset of {2} => {3}", _moduleId, num, offset, answer);
        if (answer > 99)
        {
            answer %= 100;
            Debug.LogFormat("[Fast Math #{0}] <Stage {1}> Over 99 adjustment => {2}", _moduleId, num, answer);
        }
        while (answer < 0)
        {
            answer += 50;
            Debug.LogFormat("[Fast Math #{0}] <Stage {1}> Under 0 adjustment => {2}", _moduleId, num, answer);
        }
        Debug.LogFormat("[Fast Math #{0}] <Stage {1}> Final answer => {2}", _moduleId, num, answer);
    }

    IEnumerator Countdown()
    {
        float smooth = 10;
        float delta = 1f / (threshold * smooth);
        float current = 1f;
        for (int i = 1; i <= threshold * smooth; i++)
        {
            barControl.gameObject.transform.localScale = new Vector3(1, 1, current);
            bar.material.color = Color.Lerp(Color.red, Color.green, current);
            current -= delta;
            yield return new WaitForSeconds(1f / smooth);
        }
        barControl.gameObject.transform.localScale = new Vector3(1, 1, 0f);
        HandleTimeout();
        yield return null;
    }

    void GoBtnHandle()
    {
        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, go.transform);
        go.AddInteractionPunch();

        if (!_lightsOn || _isSolved || _pressedGo) return;

        goBtn.GetComponent<MeshRenderer>().material.color = Color.gray;
        submit.GetComponent<MeshRenderer>().material.color = new Color32(229, 57, 53, 255);
        for (int i = 0; i < 10; i++)
        {
            btn[i].GetComponent<MeshRenderer>().material.color = new Color32(229, 57, 53, 255);
        }

        Debug.LogFormat("[Fast Math #{0}] Pressed GO! Let the madness begin!", _moduleId);
        StartCoroutine("Countdown");
        _pressedGo = true;
    }

    void HandlePress(int num)
    {
        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, btn[num].transform);

        if (!_lightsOn || _isSolved || digitsEntered > 1 || !_pressedGo) return;

        if (digitsEntered == 0) input += (num * 10);
        else if (digitsEntered == 1) input += num;

        digitsEntered++;
    }

    void AnsChk()
    {
        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, submit.transform);
        submit.AddInteractionPunch();

        if (!_lightsOn || _isSolved || digitsEntered < 2 || !_pressedGo) return;

        StopCoroutine("Countdown");
        Debug.LogFormat("[Fast Math #{0}] <Stage {1}> Submit: {2} Expected: {3}", _moduleId, stage, input, answer);

        if (input == answer)
        {
            Debug.LogFormat("[Fast Math #{0}] <Stage {1}> Cleared!", _moduleId, stage);
            stage++;
            if (stage > numStages)
            {
                Debug.LogFormat("[Fast Math #{0}] Module passed!", _moduleId);
                Audio.PlaySoundAtTransform("disarmed", Module.transform);
                barControl.gameObject.transform.localScale = new Vector3(1, 1, 0f);
                Screen.text = "";
                for (int i = 0; i < 10; i++)
                {
                    btn[i].GetComponent<MeshRenderer>().material.color = Color.gray;
                }
                submit.GetComponent<MeshRenderer>().material.color = Color.gray;
                Module.HandlePass();
                _isSolved = true;
            }
            else
            {
                Audio.PlaySoundAtTransform("passedStage", Module.transform);
                GenerateStage(stage);
                input = 0;
                digitsEntered = 0;
                StartCoroutine("Countdown");
            }
        }
        else
        {
            Debug.LogFormat("[Fast Math #{0}] Answer incorrect! Strike and reset!", _moduleId);
            Module.HandleStrike();
            Init();
        }
    }

    void HandleTimeout()
    {
        Debug.LogFormat("[Fast Math #{0}] Timeout! Strike and reset!", _moduleId);
        StopCoroutine("Countdown");
        Module.HandleStrike();
        Init();
    }

    int FindThreshold()
    {
        try
        {
            ModSettingsJSON settings = JsonConvert.DeserializeObject<ModSettingsJSON>(modSettings.Settings);
            if (settings != null)
            {
                if (settings.countdownTime < 5)
                    return 5;
                else if (settings.countdownTime > 60)
                    return 60;
                else return settings.countdownTime;
            }
            else return 10;
        }
        catch (JsonReaderException e)
        {
            Debug.LogFormat("[Fast Math #{0}] JSON reading failed with error {1}, using default threshold.", _moduleId, e.Message);
            return 10;
        }
    }

#pragma warning disable 414
    private readonly string TwitchHelpMessage = @"Press the GO! button with “!{0} go”. Submit the answer with “!{0} submit 05” (Must be two digits).";
#pragma warning restore 414

    KMSelectable[] ProcessTwitchCommand(string command)
    {
        command = command.ToLowerInvariant().Trim();

        if (command.Equals("go"))
            return new[] { go };

        else if (Regex.IsMatch(command, @"^submit +\d\d$"))
        {
            command = command.Substring(7).Trim();
            return new[] { btn[int.Parse(command[0].ToString())], btn[int.Parse(command[1].ToString())], submit };
        }

        return null;
    }
}
