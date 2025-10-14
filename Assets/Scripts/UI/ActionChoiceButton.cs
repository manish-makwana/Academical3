using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Anansi;
using System.Collections.Generic;

namespace Academical
{
	public class ActionChoiceButton : UIComponent
	{
		#region Fields
		[Header( "UI Elements" )]
		[SerializeField] private Button m_Button;
		[SerializeField] private TMP_Text m_Text;
		[SerializeField] private GameObject m_AuxIndicator;
		[SerializeField] private GameObject m_RequiredIndicator;
		[SerializeField] private Image m_Portrait;
		[SerializeField] private Image m_Background;
		private StoryletInstance m_Storylet;

		[SerializeField] private CharacterProfileLookup profileLookup;

		#endregion

		#region Public Methods

		public void SetStorylet(StoryletInstance storylet)
		{
			m_Storylet = storylet;
			m_Text.text = storylet.ChoiceLabel;
		}



		#endregion

		#region Protected Methods

		protected override void SubscribeToEvents()
		{
			m_Button.onClick.AddListener( OnClicked );
		}

		protected override void UnsubscribeFromEvents()
		{
			m_Button.onClick.AddListener( OnClicked );
		}

		#endregion

		#region Public Methods

		public void ShowAuxIndicator()
		{
			m_AuxIndicator.SetActive( true );
		}

		public void HideAuxIndicator()
		{
			m_AuxIndicator.SetActive( false );
		}

		public void ShowRequiredIndicator()
		{
			m_RequiredIndicator.SetActive( true );
		}

		public void HideRequiredIndicator()
		{
			m_RequiredIndicator.SetActive( false );
		}


		public void SetPortraitAndBackground()
		{
			Sprite portrait;
			profileLookup.TryGetSprite( "narrator", out portrait );
			Color background;
			profileLookup.TryGetBackground( "narrator", out background );
			//get tags
			HashSet<string> tags = m_Storylet.Storylet.Tags;
			string characterName = "";
			foreach ( string tag in tags )
			{
				//See if we have a character tag
				if ( tag.Contains( "character", StringComparison.OrdinalIgnoreCase ) )
				{
					int index = tag.IndexOf( ':' );
					if ( index >= 0 && index + 1 < tag.Length )
					{
						characterName = tag.Substring( index + 1 );
					}
				}
			}

			if ( characterName != "" )
			{
				profileLookup.TryGetSprite( characterName, out portrait );
				profileLookup.TryGetBackground( characterName, out background );
			}

			m_Portrait.sprite = portrait;
			m_Background.color = background;
		}

		#endregion

		#region Private Methods

		private void OnClicked()
		{
			GameEvents.ActionSelectModalHidden?.Invoke();
			GameEvents.LocationSelectModalHidden?.Invoke();
			GameEvents.ActionSelected?.Invoke( m_Storylet );
		}

		#endregion
	}
}
