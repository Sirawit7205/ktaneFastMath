using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using Newtonsoft.Json;
using KMHelper;

public class fastMath : MonoBehaviour {

    public class ModSettingsJSON
    {
        public int countdownTime;
        public string note;
    }

    public KMAudio Audio;
    public KMBombModule Module;
    public KMBombInfo Info;
    public KMModSettings modSettings;
    public KMSelectable[] btn;
    public KMSelectable go, submit;
    public TextMesh Screen, goText;
    public GameObject barControl;
    public MeshRenderer bar, goBtn;

    private static int _moduleIdCounter = 1;
    private int _moduleId = 0;

    private int[,] numberField = new int[13, 13] {
        {25, 11, 53, 97, 02, 42, 51, 97, 12, 86, 55, 73, 33},
        {54, 07, 32, 19, 84, 33, 27, 78, 26, 46, 09, 13, 58},
        {86, 37, 44, 01, 05, 26, 93, 49, 18, 69, 23, 40, 22},
        {54, 28, 77, 93, 11, 00, 35, 61, 27, 48, 13, 72, 80},
        {99, 36, 23, 95, 67, 05, 26, 17, 44, 60, 26, 41, 67},
        {74, 95, 03, 04, 56, 23, 54, 29, 52, 38, 10, 76, 98},
        {88, 46, 37, 96, 02, 52, 81, 37, 12, 70, 14, 36, 78},
        {54, 43, 12, 65, 94, 03, 47, 23, 16, 62, 73, 46, 21},
        {07, 33, 26, 01, 67, 26, 27, 77, 83, 14, 27, 93, 09},
        {63, 64, 94, 27, 48, 84, 33, 10, 16, 74, 43, 99, 04},
        {35, 39, 03, 25, 47, 62, 38, 45, 88, 48, 34, 31, 27},
        {67, 30, 27, 71, 09, 11, 44, 37, 18, 40, 32, 15, 78},
        {13, 23, 26, 85, 92, 12, 73, 56, 81, 07, 75, 47, 99}
    };
    private string letters = "ABCDEGKNPSTXZ";
    private bool _isSolved = false, _lightsOn = false, _pressedGo = false;
    private int stageAmt, stageCur = 1, ans, inputAns = 0, threshold = 10, digits = 0;

    void Start ()
    {
        _moduleId = _moduleIdCounter++;
        Module.OnActivate += Activate;
	}

    private void Awake()
    {
        go.OnInteract += delegate ()
        {
            goBtnHandle();
            return false;
        };
        submit.OnInteract += delegate ()
        {
            ansChk();
            return false;
        };
        for (int i = 0; i < 10; i++)
        {
            int j = i;
            btn[i].OnInteract += delegate ()
            {
                handlePress(j);
                return false;
            };
        }
    }

    void Activate()
    {
		Init();
        _lightsOn = true;
    }

    void Init()
    {
        stageAmt = Random.Range(3, 6);
        Debug.LogFormat("[Fast Math #{0}] This module will have {1} stages.", _moduleId, stageAmt);
        threshold = findThreshold();
        Debug.LogFormat("[Fast Math #{0}] Threshold time set to {1} seconds.", _moduleId, threshold);
        generateStage(1);

        //var reset
        _pressedGo = false;
        stageCur = 1;
        inputAns = 0;
        digits = 0;
        goBtn.material.color = new Color32(229, 57, 53, 255);
        submit.GetComponent<MeshRenderer>().material.color = Color.gray;
        for (int i = 0; i < 10; i++)
        {
            btn[i].GetComponent<MeshRenderer>().material.color = Color.gray;
        }
    }

    void generateStage(int num)
    {
        int randLeft = Random.Range(0, 13), randRight = Random.Range(0, 13);

        Debug.LogFormat("[Fast Math #{0}] <Stage {1}> Start!", _moduleId, num);
        Debug.LogFormat("[Fast Math #{0}] <Stage {1}> Characters are {2}{3}", _moduleId, num, letters[randLeft], letters[randRight]);

        Screen.text = letters[randLeft] + " " + letters[randRight];

        ans = numberField[randLeft, randRight];
        Debug.LogFormat("[Fast Math #{0}] <Stage {1}> Numbers in the table is {2}", _moduleId, num, ans);
        if (Info.IsIndicatorOn(KMBombInfoExtensions.KnownIndicatorLabel.MSA))
        {
            ans += 20;
            Debug.LogFormat("[Fast Math #{0}] <Stage {1}> Lit MSA: +20 => {2}", _moduleId, num, ans);
        }
        if(Info.IsPortPresent(KMBombInfoExtensions.KnownPortType.Serial))
        {
            ans += 14;
            Debug.LogFormat("[Fast Math #{0}] <Stage {1}> Serial port: +14 => {2}", _moduleId, num, ans);
        }
        if(Info.GetSerialNumberLetters().Any("FAST".Contains))
        {
            ans -= 5;
            Debug.LogFormat("[Fast Math #{0}] <Stage {1}> S/N contains FAST: -5 => {2}", _moduleId, num, ans);
        }
        if(Info.IsPortPresent(KMBombInfoExtensions.KnownPortType.RJ45))
        {
            ans += 27;
            Debug.LogFormat("[Fast Math #{0}] <Stage {1}> RJ-45 port: +27 => {2}", _moduleId, num, ans);
        }
        if(Info.GetBatteryCount() > 3)
        {
            ans -= 15;
            Debug.LogFormat("[Fast Math #{0}] <Stage {1}> More than 3 batt: -15 => {2}", _moduleId, num, ans);
        }
        if(ans > 99)
        {
            ans %= 100;
            Debug.LogFormat("[Fast Math #{0}] <Stage {1}> Over 99 adjustment => {2}", _moduleId, num, ans);
        }
        if(ans < 0)
        {
            ans += 50;
            Debug.LogFormat("[Fast Math #{0}] <Stage {1}> Under 0 adjustment => {2}", _moduleId, num, ans);
        }
        Debug.LogFormat("[Fast Math #{0}] <Stage {1}> Final answer => {2}", _moduleId, num, ans);
    }

    IEnumerator countdown()
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
        handleTimeout();
        yield return null;
    }

    void goBtnHandle()
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

        Debug.LogFormat("[Fast Math #{0}] Pressed GO! Let the madness begin!",_moduleId);
        StartCoroutine("countdown");
        _pressedGo = true;
    }

    void handlePress(int num)
    {
        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, btn[num].transform);

        if (!_lightsOn || _isSolved || digits > 1 || !_pressedGo ) return;

        if (digits == 0) inputAns += (num * 10);
        else if (digits == 1) inputAns += num;

        digits++;
    }

    void ansChk()
    {
        Audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.ButtonPress, submit.transform);
        submit.AddInteractionPunch();

        if (!_lightsOn || _isSolved || digits < 2 || !_pressedGo ) return;

        StopCoroutine("countdown");
        Debug.LogFormat("[Fast Math #{0}] <Stage {1}> Submit: {2} Expected: {3}", _moduleId, stageCur, inputAns, ans);

        if(inputAns == ans)
        {
            Debug.LogFormat("[Fast Math #{0}] <Stage {1}> Cleared!", _moduleId, stageCur);
            stageCur++;
            if(stageCur > stageAmt)
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
                generateStage(stageCur);
                inputAns = 0;
                digits = 0;
                StartCoroutine("countdown");
            }
        }
        else
        {
            Debug.LogFormat("[Fast Math #{0}] Answer incorrect! Strike and reset!", _moduleId);
            Module.HandleStrike();
            Init();
        }
    }

    void handleTimeout()
    {
        Debug.LogFormat("[Fast Math #{0}] Timeout! Strike and reset!", _moduleId);
        StopCoroutine("countdown");
        Module.HandleStrike();
        Init();
    }

    int findThreshold()
    {
        try
        {
            ModSettingsJSON settings = JsonConvert.DeserializeObject<ModSettingsJSON>(modSettings.Settings);
            if (settings != null)
            {
                if (settings.countdownTime < 5)
                    return 5;
                else if (settings.countdownTime > 30)
                    return 30;
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
