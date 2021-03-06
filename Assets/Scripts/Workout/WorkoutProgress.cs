﻿using DG.Tweening;
using Newtonsoft.Json.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class WorkoutProgress : Singleton<WorkoutProgress>
{
    public enum State
    {
        Finished,
        GetReady,
        InProgress,
        Paused,
        Cancelled
    }
    public State state = State.Finished;

    //Is state changed by user or by program
    public enum StateAction
    {
        ByProgram,
        ByUser,
    }
    public StateAction stateAction = StateAction.ByUser;

    //TODO: Make somekind of mvc data class?
    public ListController listController;

    public GameObject pausedOverlay;

    [Header("Text Fields")]
    public TMP_Text setText;
    public TMP_Text exerciseText;
    public Timetext timerText;
    public TMP_Text repsText;

    [Header("Next")]
    public Image nextBackground;    
    public TMP_Text nextExerciseText;

    [Header("Buttons")]
    public Button pauseButton;
    public Button playButton;
    public Button repeatButton;    
    public Button nextButton;
    public LinkButton infoButton;

    [Header("Background")]
    public Image backgroundImage;
    private Color backgroundColor; //For lerping color

    [Header("Background Colors")]
    public Color readyColor;
    public Color workColor;
    public Color restColor;
    public Color finishedColor;
    public Color nextBackgroundColor;

    [Header("Audio")]
    public AudioSource secondBeep; //Beep sound for each countdown second
    public AudioSource roundBeep;  //Beep sound after each round

    public WorkoutProgressBar progressBar;
    public int countdownSeconds = 5;

    //Foot buttons layout for on orientation change logic
    public HorizontalOrVerticalLayoutGroup footerButtonsLayout;

    //These are floats because of the percentage calculation
    private float totalWorkoutTime;
    private float workouteTimePassed;

    //Old field values when switching from portrait to landscape
    private RectOffset footerButtonsRectOffset;
    private float setTextMax;
    private float exerciseTextMax;
    private float timerTextMax;
    private float repsTextMax;

    private WorkoutPanel.Workout currentWorkout;

    //Both true as the workout has not started yet on app start
    private bool paused = true;
    private bool cancelled = true;
    private bool skip = false;

    private int currentSet;
    private int exerciseCount;  //Reset to 0 each set
    private ExerciseDataItem currentExercise;
    private List<KeyValuePair<string, ExerciseDataItem>> _exerciseList;
    //private List<ExerciseDataItem> _randomExerciseList;

    private string getReadyString = "Get Ready";
    private bool landscapeWasEnabled;

    private void Start()
    {
        landscapeWasEnabled = false;
        footerButtonsRectOffset = footerButtonsLayout.padding;

        setTextMax = setText.fontSizeMax;
        exerciseTextMax = exerciseText.fontSizeMax;
        timerTextMax = timerText.tmp.fontSizeMax;
        repsTextMax = repsText.fontSizeMax;
    }

    private void Update()
    {
        backgroundImage.color = Color.Lerp(backgroundImage.color, backgroundColor, Time.deltaTime * 1);
    }

    public void OnEnable()
    {
        StartCoroutine(orientationChangedRoutine());
    }

    public void OnDisable()
    {
        StopCoroutine(orientationChangedRoutine());
    }

    /// <summary>
    /// Note that this will not work in-editor
    /// It will always return DeviceOrientation.Unknown
    /// </summary>
    /// <returns></returns>
    IEnumerator orientationChangedRoutine()
    {
        float waitForChange = 0.2f;
        float waitForScreenToTurn = 0.2f;

        while (gameObject.activeSelf)
        {
//#if !UNITY_EDITOR
            switch (Input.deviceOrientation)
            {
                case DeviceOrientation.Portrait:            yield return new WaitForSeconds(waitForScreenToTurn); onPortrait(); break;
                case DeviceOrientation.PortraitUpsideDown:  yield return new WaitForSeconds(waitForScreenToTurn); onPortrait(); break;
                case DeviceOrientation.LandscapeLeft:       yield return new WaitForSeconds(waitForScreenToTurn); onLandscape(); break;
                case DeviceOrientation.LandscapeRight:      yield return new WaitForSeconds(waitForScreenToTurn); onLandscape(); break;
            }
//#endif
            yield return new WaitForSeconds(waitForChange);
        }
    }

    private void onPortrait()
    {
        if (landscapeWasEnabled)
        {
            landscapeWasEnabled = false;
            setText.fontSizeMax = setTextMax;
            exerciseText.fontSizeMax = exerciseTextMax;
            timerText.tmp.fontSizeMax = timerTextMax;
            repsText.fontSizeMax = repsTextMax;

            footerButtonsLayout.padding = footerButtonsRectOffset;
        }
    }

    private void onLandscape()
    {
        landscapeWasEnabled = true;
        setText.fontSizeMax = setTextMax / 2;
        exerciseText.fontSizeMax = exerciseTextMax / 2;
        timerText.tmp.fontSizeMax = timerTextMax / 2;
        repsText.fontSizeMax = repsTextMax / 2;

        footerButtonsLayout.padding = new RectOffset(0, 5, 5, 0);
    }

    private void togglePausePlayButton()
    {
        pauseButton.gameObject.SetActive(!paused);
        pauseButton.interactable =!paused;
        playButton.gameObject.SetActive(paused);
        playButton.interactable = paused;
    }

    public bool isInProgress()
    {
        return state == State.InProgress || state == State.Paused;
    }

    public void cancel()
    {
        setState(State.Cancelled);
        StopCoroutine("workoutLoop");
        PanelNavigator.Instance.goToPreviousInHistory();
    }

    public void play()
    {
        setState(State.InProgress);
        paused = false;
    }

    /// <summary>
    /// Pause by user with button, cannot have param because this doesn't work in inspector
    /// DO NOT USE IN CODE
    /// </summary>
    public void pause()
    {
        stateAction = StateAction.ByUser;
        setState(State.Paused);
        paused = true;
    }

    /// <summary>
    /// Pause by program
    /// </summary>
    /// <param name="stateAction"></param>
    public void pause2()
    {
        stateAction = StateAction.ByProgram;
        setState(State.Paused);
        paused = true;
    }

    public void restart()
    {
        startWorkout(currentWorkout);
    }

    public void startWorkout(WorkoutPanel.Workout workout)
    {
        currentWorkout = workout;
        StartCoroutine(workoutCountdown());
    }

    private IEnumerator workoutCountdown()
    {
        setState(State.GetReady);

        int seconds = countdownSeconds;
        while(seconds > 0)
        {
            secondBeep.Play();
            timerText.setTimeText(seconds);
            playTimeTextAnimation();
            yield return new WaitForSecondsRealtime(1.0f);
            seconds--;
        }
        exerciseText.text = "";
        timerText.setText("");

        startWorkout();
    }

    private void startWorkout()
    {
        if (currentWorkout.exerciseMode == WorkoutPanel.ExerciseMode.Random)
        {
            _exerciseList = new List<KeyValuePair<string, ExerciseDataItem>>();
            _exerciseList = listController.loadExercisesData();
        }
        else
        {
            _exerciseList = currentWorkout.exercises.ToList();
        }

        switch (currentWorkout.workoutMode)
        {
            case WorkoutPanel.WorkoutMode.time: StartCoroutine(workoutRoutine()); break;
            case WorkoutPanel.WorkoutMode.reps: nextExercise(); break;
        }
    }

    public void nextExercise()
    {
        setState(State.InProgress);

        switch (currentWorkout.exerciseMode)
        {
            case WorkoutPanel.ExerciseMode.Selection: if (exerciseCount == _exerciseList.Count()) currentSet++; break;
            case WorkoutPanel.ExerciseMode.Random: if (exerciseCount == currentWorkout.roundsPerSet) currentSet++; break;
            case WorkoutPanel.ExerciseMode.Rounds: if (exerciseCount == currentWorkout.roundsPerSet) currentSet++; break;
        }

        if (currentSet == currentWorkout.globalSets) setState(State.Finished);
        else
        {
            //If in random/rounds mode there are no exercise data items
            //Set exercise based on workout exercise mode
            switch (currentWorkout.exerciseMode)
            {
                case WorkoutPanel.ExerciseMode.Selection: setCurrentExercise(_exerciseList[exerciseCount].Value, currentWorkout.globalReps); break;
                case WorkoutPanel.ExerciseMode.Random: setCurrentExercise(getRandomExercise(), currentWorkout.globalReps); break;
                case WorkoutPanel.ExerciseMode.Rounds: setCurrentExercise("Work", currentWorkout.globalReps); break;
            }

            exerciseCount++;

            //If in Selection mode use exerciseCount for progress calculation, otherwise always use roundsPerSet
            int sets = currentWorkout.globalSets == 0 ? 1 : currentWorkout.globalSets;
            int total = currentWorkout.exerciseMode == WorkoutPanel.ExerciseMode.Selection ? _exerciseList.Count() * sets : currentWorkout.roundsPerSet * sets;
            progressBar.setProgress(((float)exerciseCount / total));
        }
    }

    private IEnumerator workoutRoutine()
    {
        //If no exercises selected get rounds per set value
        bool noExercisesSelected = currentWorkout.roundsPerSet > 0 && currentWorkout.exercises.Count == 0;
        int exerciseTotal = noExercisesSelected ? currentWorkout.roundsPerSet :  currentWorkout.exercises.Count;

        totalWorkoutTime = currentWorkout.getTotalWorkoutTimeInSeconds(exerciseTotal);
        workouteTimePassed = 0;

        for (currentSet = 0; currentSet < currentWorkout.globalSets; currentSet++)
        {
            pauseButton.interactable = false;
            setText.text = (currentSet + 1).ToString() + " / " + currentWorkout.globalSets;
            if(currentSet > 1) playSetTextAnimation();
            
            bool isSetRest = currentSet > 0; //Reset between sets if we passed set 0

            for(exerciseCount = 0; exerciseCount < exerciseTotal; exerciseCount++)
            {
                //Null and default if no exercises selected
                KeyValuePair<string, ExerciseDataItem> pair = noExercisesSelected ? default : _exerciseList[exerciseCount];
                ExerciseDataItem exerciseData = noExercisesSelected ? null : pair.Value;

                if (isSetRest || isExerciseRest(exerciseTotal))
                {
                    int restSeconds = currentWorkout.globalRestTime;
                    setRest(restSeconds);

                    //Show next exercise for modes Selection and Random
                    if (currentWorkout.exerciseMode != WorkoutPanel.ExerciseMode.Rounds)
                    {
                        //Be sure to set theNextExercise before calling showNextExercise
                        switch (currentWorkout.exerciseMode)
                        {
                            case WorkoutPanel.ExerciseMode.Random: currentExercise = getRandomExercise(); break;
                            case WorkoutPanel.ExerciseMode.Selection: currentExercise = exerciseData; break;
                        }
                        showNextExercise();
                    }

                    while (restSeconds > 0)
                    {
                        if (!pauseButton.interactable) pauseButton.interactable = true;
                        if (restSeconds <= 3)
                        {
                            playTimeTextAnimation();
                            secondBeep.Play();
                        }

                        timerText.setTimeText(restSeconds);
                        float second = 0;
                        while(second < 1)
                        {
                            yield return new WaitForSecondsRealtime(0.1f);
                            second += 0.1f;
                            if (paused)
                            {
                                setState(State.Paused);
                                yield return new WaitUntil(() => !paused);
                            }
                        }
                        
                        workouteTimePassed++;
                        restSeconds--;
                        progressBar.setProgress(workouteTimePassed / totalWorkoutTime);                        
                    }
                }

                int workoutSeconds = currentWorkout.globalWorkTime;

                //Set exercise based on workout exercise mode
                switch(currentWorkout.exerciseMode)
                {
                    //Use currentExercise after rest otherwise use exerciseData for the first time
                    case WorkoutPanel.ExerciseMode.Random:      setCurrentExercise(currentExercise ?? getRandomExercise(), workoutSeconds); break;
                    case WorkoutPanel.ExerciseMode.Rounds:      setCurrentExercise("Work", workoutSeconds); break;
                    case WorkoutPanel.ExerciseMode.Selection:   setCurrentExercise(currentExercise ?? exerciseData, workoutSeconds); break;
                }

                roundBeep.Play();
                yield return new WaitForSecondsRealtime(roundBeep.clip.length);

                while (workoutSeconds > 0)
                {
                    if (!pauseButton.interactable) pauseButton.interactable = true;
                    if (workoutSeconds <= 3)
                    {
                        playTimeTextAnimation();
                        secondBeep.Play();
                    }

                    timerText.setTimeText(workoutSeconds);
                    float second = 0;
                    while(second < 1)
                    {                        
                        yield return new WaitForSecondsRealtime(0.1f);
                        second += 0.1f;
                        if (paused)
                        {
                            setState(State.Paused);
                            yield return new WaitUntil(() => !paused);
                        }
                    }

                    workouteTimePassed++;
                    workoutSeconds--;
                    progressBar.setProgress(workouteTimePassed / totalWorkoutTime);                    
                }
                
                roundBeep.Play();                
            }
        }

        setState(State.Finished);
    }

    private void playExerciseTextAnimation()
    {
        exerciseText.transform.DOPunchScale(new Vector2(0.2f, 0.2f), 0.5f);
    }

    private void playTimeTextAnimation()
    {
        timerText.transform.DOPunchScale(new Vector2(0.15f, 0.15f), 0.5f);
    }

    private void playSetTextAnimation()
    {
        setText.transform.DOPunchScale(new Vector2(0.2f, 0.2f), 0.5f);
    }

    private ExerciseDataItem getRandomExercise()
    {        
        return _exerciseList[Random.Range(0, _exerciseList.Count())].Value;

    }

    private bool isExerciseRest(int exerciseTotal)
    {
        bool notFirstExercise = exerciseCount > 0;
        bool notLastExercise = exerciseCount < exerciseTotal;
        return (notFirstExercise && notLastExercise);
    }

    private void setRest(int time, string text = "Rest")
    {
        exerciseText.text = text.ToUpper();
        playExerciseTextAnimation();

        backgroundColor = restColor;
        setTimeText(time);
    }

    private void showNextExercise()
    {
        nextBackground.color = nextBackgroundColor;
        nextExerciseText.text = currentExercise.name.ToUpper();
    }

    private void setCurrentExercise(string name, int time)
    {
        setState(State.InProgress);
        currentExercise = null;
        exerciseText.text = name.ToUpper();
        playExerciseTextAnimation();
        setTimeText(time);
    }

    private void setCurrentExercise(ExerciseDataItem exercise, int timeOrReps)
    {
        setState(State.InProgress);
        currentExercise = exercise;
        exerciseText.text = currentExercise.name.ToUpper();
        playExerciseTextAnimation();
        setTimeText(timeOrReps);
    }

    public void searchExercise()
    {        
        if(currentWorkout.workoutMode == WorkoutPanel.WorkoutMode.time) pause();
        if(currentExercise != null)
        {
            string searchQuery = currentExercise.name + " exercise";
            string imagesParameter = "&iax=images&ia=images";
            infoButton.appendToURL(searchQuery + imagesParameter);
            infoButton.openUrl();
        }       
    }

    private void setTimeText(int value)
    {
        switch (currentWorkout.workoutMode)
        {
            case WorkoutPanel.WorkoutMode.time: timerText.setTimeText(value); break;
            case WorkoutPanel.WorkoutMode.reps: repsText.text = value.ToString(); break;
        }
    }

    private void setState(State workoutState)
    {
        state = workoutState;
        switch (state)
        {
            //Seperate functions because of possible nested switch cases
            case State.GetReady:    setGetReady(); break;
            case State.InProgress:  setInProgressState(); break;
            case State.Paused:      setPausedState(); break;
            case State.Cancelled:   setCancelledState(); break;
            case State.Finished:    setFinishedState(); break;
        }

    }

    private void setGetReady()
    {
        paused = false;
        pausedOverlay.SetActive(paused);

        cancelled = false;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        exerciseText.text = "";
        nextExerciseText.text = "";

        repeatButton.interactable = false;
        pauseButton.interactable = false;
        playButton.interactable = false;
        nextButton.interactable = false;
        infoButton.button.interactable = false;
        nextBackground.color = new Color(0, 0, 0, 0);
        
        progressBar.setProgress(0, false);

        setText.text = "";
        timerText.setText("");

        exerciseText.text = getReadyString.ToUpper();
        playExerciseTextAnimation();        

        backgroundColor = readyColor;
    }

    private void setInProgressState()
    {
        paused = false;
        pausedOverlay.SetActive(paused);

        cancelled = false;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        togglePausePlayButton();

        bool workoutRepsMode = currentWorkout.workoutMode == WorkoutPanel.WorkoutMode.reps;
        nextButton.gameObject.SetActive(workoutRepsMode);
        nextButton.interactable = workoutRepsMode;

        bool workoutModeTimed = currentWorkout.workoutMode == WorkoutPanel.WorkoutMode.time;
        pauseButton.gameObject.SetActive(workoutModeTimed);
        pauseButton.interactable = workoutModeTimed;

        bool exerciseModeRounds = currentWorkout.exerciseMode == WorkoutPanel.ExerciseMode.Rounds;
        infoButton.gameObject.SetActive(!exerciseModeRounds);
        infoButton.button.interactable = !exerciseModeRounds;

        nextExerciseText.text = "";
        backgroundColor = workColor;
        nextBackground.color = new Color(0, 0, 0, 0);
    }

    private void setPausedState()
    {
        if(currentWorkout.workoutMode == WorkoutPanel.WorkoutMode.time) togglePausePlayButton();
        pausedOverlay.SetActive(paused);
    }

    private void setCancelledState()
    {
        paused = true;
        cancelled = true;
        Screen.sleepTimeout = SleepTimeout.SystemSetting;
    }

    private void setFinishedState()
    {
        //Vars
        currentSet = 0;
        exerciseCount = 0;
        currentExercise = null;

        //Images
        backgroundColor = finishedColor;
        nextBackground.color = new Color(0, 0, 0, 0);

        //Text
        exerciseText.text = "Finished!";
        timerText.setText("");

        //Buttons
        repeatButton.gameObject.SetActive(true);
        repeatButton.interactable = true;
        nextButton.interactable = false;
        infoButton.button.interactable = false;
        playButton.interactable = false;
        pauseButton.interactable = false;
    }
}
