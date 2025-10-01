using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Academical
{
	public class DialogueHistoryEntryUI : UIComponent
	{
		#region Fields

		[Header( "UI Elements" )]
		[SerializeField] private TMP_Text m_SpeakerName;
		[SerializeField] private TMP_Text m_DialogueText;

		[SerializeField]
		CharacterProfileLookup portraitMapping;

		[SerializeField]
		Image portrait;

		[SerializeField]
		Image background;

		#endregion

		#region Public Methods

		public void SetSpeaker(string speaker)
		{
			m_SpeakerName.text = speaker;
		}

		public void SetText(string text)
		{
			m_DialogueText.text = text;
		}

		public void SetCharacterPortrait(string characterName)
		{
			if ( portraitMapping.TryGetSprite( characterName, out var sprite ) )
				portrait.sprite = sprite;
			else
				Debug.LogWarning( $"No sprite for '{characterName}'" );
				
			if (portraitMapping.TryGetBackground(characterName, out var backgroundColor))
            	background.color = backgroundColor;
        	else
            	Debug.LogWarning($"No background color for '{characterName}'");
    	}   

		#endregion
	}
}
