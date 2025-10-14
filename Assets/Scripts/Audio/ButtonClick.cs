using System.Collections;
using System.Collections.Generic;
using Academical;
using UnityEngine;
using UnityEngine.UI;

public class ButtonClick : MonoBehaviour
{
    public Button button;
    public bool isSecondaryClick;

    void Start()
    {
        if ( isSecondaryClick )
        {
            button.onClick.AddListener( PlayDialogueSoundOnClick );
        }
        else
        {
            button.onClick.AddListener( PlaySoundOnClick );
        }
    }

    public void PlaySoundOnClick()
    {
        AudioManager.PlayDefaultButtonSound();
    }

    public void PlayDialogueSoundOnClick()
    {
        AudioManager.PlayDialogueButtonSound();
    }

	void OnDestroy()
	{
        button.onClick.RemoveListener( PlaySoundOnClick );
	}
}
