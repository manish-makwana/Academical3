using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text.RegularExpressions;
using Academical.Persistence;
using Anansi;
using Ink.Parsed;
using Newtonsoft.Json.Schema;
using RePraxis;
using TDRS;
using TDRS.Serialization;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Academical
{
	/// <summary>
	/// The game
	/// </summary>
	[DefaultExecutionOrder( 2 )]
	public class GameManager : MonoBehaviour
	{
		[Tooltip( "Toggle auto saving after each conversation." )]
		[SerializeField]
		public bool m_AutoSaveEnabled;

		[SerializeField]
		private Character m_Player;

		private Location m_CurrentLocation;

		/// <summary>
		/// Manages the underlying world simulation.
		/// </summary>
		[SerializeField]
		private SimulationController m_simulationController;

		/// <summary>
		/// Manages character relationships and all information in the logical database.
		/// </summary>
		[SerializeField]
		private SocialEngineController m_socialEngine;

		/// <summary>
		/// Manages when/how the background overlay is displayed.
		/// </summary>
		[SerializeField]
		private GameObject m_backgroundOverlay;

		/// <summary>
		/// A reference to the script controlling the dialogue and story progression.
		/// </summary>
		[SerializeField]
		private DialogueManager m_dialogueManager;

		/// <summary>
		/// All storylets related to locations on the map.
		/// </summary>
		private Dictionary<string, Storylet> m_locationStorylets;

		/// <summary>
		/// Storylets that can be triggered when a player enters a location.
		/// </summary>
		private Dictionary<string, List<Storylet>> m_LocationEnterStorylets;

		/// <summary>
		/// Storylets that can be triggered when the player leaves a location.
		/// </summary>
		private Dictionary<string, List<Storylet>> m_LocationExitStorylets;

		/// <summary>
		/// All storylets related to actions the player can take at locations.
		/// </summary>
		private Dictionary<string, Storylet> m_actionStorylets;

		#region Properties

		public static GameManager Instance { get; private set; }

		/// <summary>
		/// A reference to the simulations database.
		/// </summary>
		public RePraxis.RePraxisDatabase DB => m_socialEngine.DB;

		/// <summary>
		/// A reference to the dialogue manager.
		/// </summary>
		public DialogueManager DialogueManager => m_dialogueManager;

		public Character Player => m_Player;

		public Location CurrentLocation => m_CurrentLocation;

		public SimDateTime CurrentDate => m_simulationController.DateTime;

		[SerializeField]
		public Button m_LocationButton;

		[SerializeField]
		public Button m_ActionButton;

		#endregion

		private void Awake()
		{
			if ( Instance != null )
			{
				Destroy( gameObject );
				return;
			}

			Instance = this;
			m_locationStorylets = new Dictionary<string, Storylet>();
			m_actionStorylets = new Dictionary<string, Storylet>();
			m_LocationEnterStorylets = new Dictionary<string, List<Storylet>>();
			m_LocationExitStorylets = new Dictionary<string, List<Storylet>>();
		}

		private void OnEnable()
		{
			GameEvents.LocationSelectModalShown += OnLocationSelectModalShown;
			GameEvents.ActionSelectModalShown += OnActionSelectModalShown;
			GameEvents.ActionSelected += OnActionSelected;
			GameEvents.OnGameSaved += OnGameSaved;
		}

		private void OnDisable()
		{
			GameEvents.LocationSelectModalShown -= OnLocationSelectModalShown;
			GameEvents.ActionSelectModalShown -= OnActionSelectModalShown;
			GameEvents.ActionSelected -= OnActionSelected;
			GameEvents.OnGameSaved -= OnGameSaved;
		}

		private void Start()
		{
			SaveData saveData = DataPersistenceManager.SaveData;
			GameState gameState = GameStateManager.GetGameState();
			GameLevelSO currentLevel = GameStateManager.Instance.LevelData;

			if ( currentLevel != null )
			{
				m_dialogueManager.SetStory( new Anansi.Story( currentLevel.inkScript.text ) );
			}

			GameEvents.GameHUDShown?.Invoke();
			SocialEngineController.Instance.Initialize();
			SocialEngineController.Instance.RegisterAgentsAndRelationships();
			m_simulationController.Initialize();

			m_dialogueManager.Initialize();


			RegisterExternalInkFunctions( m_dialogueManager.Story.InkStory );

			this.m_actionStorylets = m_dialogueManager.Story
				.GetStoryletsWithTags( "action" )
				.ToDictionary( s => s.ID );

			this.m_locationStorylets = m_dialogueManager.Story
				.GetStoryletsWithTags( "location" )
				.ToDictionary( s => s.ID );

			m_Player.Location = null;

			if ( saveData != null )
			{
				PlayTimeTracker.Instance.SetTotalPlayTime( saveData.totalPlaytime );
				m_simulationController.DateTime = new SimDateTime( saveData.currentDay, Enum.Parse<TimeOfDay>( saveData.currentTimeOfDay ) );
				//if we don't have a location, it means we are at a narration point at the beginning of the day
				//This is a hacky solution, but we don't need extensibility for the time being.
				if ( saveData.currentLocationId != null || saveData.currentLocationId != "Undefined" )
				{
					SetPlayerLocation( saveData.currentLocationId );
				}
				LoadDatabaseSave( saveData );
				LoadStoryState( saveData );
				LoadSocialEngineState( saveData );
			}

			//Set day label here, as Anansi doesn't natively support day labels.
			m_simulationController.DateTime.DayEventLabel = DateLabelConstants.GetLabelForDay( m_simulationController.DateTime.Day );
			m_dialogueManager.Story.DB = SocialEngineController.Instance.DB;

			StartStory();
		}

		private void LoadDatabaseSave(SaveData data)
		{
			foreach ( string entry in data.databaseEntries )
			{
				m_socialEngine.DB.Insert( entry );
			}
		}

		private void LoadStoryState(SaveData data)
		{
			m_dialogueManager.Story.InkStory.state.LoadJson( data.storyData.inkJson );
			foreach ( StoryletData entry in data.storyData.storylets )
			{
				Storylet storylet = m_dialogueManager.Story.GetStorylet( entry.storyletId );
				storylet.CooldownTimeRemaining = entry.cooldown;
				storylet.TimesPlayed = entry.timesVisited;
			}
		}

		private void LoadSocialEngineState(SaveData data)
		{
			// m_socialEngine.State
		}

		/// <summary>
		/// Start the story.
		/// </summary>
		public void StartStory()
		{
			GameEvents.OnStoryStart?.Invoke();

			if ( DataPersistenceManager.SaveData == null )
			{
				if ( m_dialogueManager.Story.StoryletExists( "start" ) )
				{
					Storylet startStorylet = m_dialogueManager.Story.GetStorylet( "start" );
					m_dialogueManager.RunStorylet( startStorylet );
				}
			}
			else
			{
				//If we are loading from a save file, we replay the start-of-day narration for a player refresher.
				TriggerDayIntroDialogue( m_simulationController.DateTime.Day );
			}
		}

		/// <summary>
		/// Set the player's current location and change the background
		/// </summary>
		/// <param name="locationID"></param>
		/// <param name="tags"></param>
		public void SetPlayerLocation(string locationID, bool runStorylets = true)
		{
			//TODO: This is a hacky fix to existing broken code. We should eventually refactor/design the overlay entirely.
			//If this is the first time we are moving to a location, update alpha to 1. 
			if ( m_backgroundOverlay.GetComponent<CanvasGroup>().alpha == 0 )
			{
				m_backgroundOverlay.GetComponent<CanvasGroup>().alpha = 1;
			}

			Location location = m_simulationController.GetLocation( locationID );

			if ( m_Player.Location != location )
			{
				if ( m_Player.Location != null )
				{
					GameEvents.OnLocationExit?.Invoke( m_Player.Location );
				}

				m_CurrentLocation = location;
				m_simulationController.SetCharacterLocation( m_Player, location );
				m_dialogueManager.SetBackground(
					new BackgroundInfo(
						locationID,
						new string[]
						{
							// Pass the time of day as an optional tag.
							$"~{m_simulationController.DateTime.TimeOfDay.ToString()}"
						}
					)
				);

				//handle background audio - we have to use a lookup as Anansi doesn't allow assigning metadata (e.g. background music) to locations.
				string backgroundMusicLabel = MusicLookup.GetMusicLabelForLocationID( locationID );
				if ( backgroundMusicLabel != null)
				{
					AudioManager.CrossfadeMusicTo( backgroundMusicLabel, fadeSeconds: 1f, loop: true, volume: 1 );
				}


				GameEvents.OnLocationEnter?.Invoke( location );
			}
		}

		/// <summary>
		/// Change the player's location and the current story location.
		/// </summary>
		/// <param name="location"></param>
		public void ChangeLocation(Location location)
		{
			if ( m_Player.Location == location ) return;
			GameEvents.OnFadeToBlack?.Invoke( 2f );
						//Wait for fade back in
			GameEvents.OnFadeFromBlack?.Invoke( 2f );





			if ( m_Player.Location != null )
			{
				GameEvents.OnLocationExit?.Invoke( m_Player.Location );
			}

			m_CurrentLocation = location;
			m_simulationController.SetCharacterLocation( m_Player, location );
			m_dialogueManager.SetBackground(
					new BackgroundInfo(
						location.UniqueID,
						new string[]
						{
							// Pass the time of day as an optional tag.
							$"~{m_simulationController.DateTime.TimeOfDay.ToString()}"
						}
					)
				);

			//handle background audio - we have to use a lookup as Anansi doesn't allow assigning metadata (e.g. background music) to locations.
			string backgroundMusicLabel = MusicLookup.GetMusicLabelForLocationID( location.UniqueID );
			if ( backgroundMusicLabel != null )
			{
				AudioManager.CrossfadeMusicTo( backgroundMusicLabel, fadeSeconds: 2f, loop: true, volume: 1 );
			}


			//Disable button, show blank dialogue if no action storylets exist
			List<ActionStoryletInfo> locationStorylets = GetEligibleActionStorylets( m_CurrentLocation );

			//TODO: Don't hardcode this.
			bool validLocation = m_CurrentLocation.UniqueID != "" && m_CurrentLocation.UniqueID != "bedroom";

			if ( locationStorylets.Count <= 0 && validLocation )
			{
				DisableActionButton();
				Storylet EmptyLocationStorylet = m_dialogueManager.Story.GetStorylet( m_CurrentLocation.UniqueID );
				m_dialogueManager.RunStorylet( EmptyLocationStorylet );
			}
			else
			{
				EnableActionButton();
			}
			
			

		}

		public StoryletInstance SelectStoryletFromCollection(IEnumerable<Storylet> storylets)
		{
			List<StoryletInstance> storyletInstances = new List<StoryletInstance>();

			foreach ( Storylet storylet in storylets )
			{
				List<StoryletInstance> instances = m_dialogueManager.Story.CreateStoryletInstances(
					storylet,
					new Dictionary<string, object>()
				);

				foreach ( var entry in instances )
				{
					storyletInstances.Add( entry );
				}
			}

			if ( storyletInstances.Count > 0 )
			{
				StoryletInstance selectedInstance = storyletInstances
					.RandomElementByWeight( s => s.Weight );

				return selectedInstance;
			}

			return null;
		}

		/// <summary>
		/// Advance the game to the next day.
		/// </summary>
		public void AdvanceDay()
		{
			StartCoroutine( AdvanceDayCoroutine() );
		}

		private IEnumerator AdvanceDayCoroutine()
		{
			//Change to end day music
			//TODO: shouldn't hard code this label.
			string endDay = MusicLookup.GetMusicLabelForLocationID( "end_day" );
			AudioManager.CrossfadeMusicTo( endDay, fadeSeconds: 0f, loop: false, volume: 1 );
			//Fade to black
			GameEvents.OnFadeToBlack?.Invoke( .5f );
			//Wait for fade back in



			yield return new WaitForSeconds( 6.5f );



			GameEvents.OnFadeFromBlack?.Invoke( .5f );
			
			//Fetch day event label based on our constants defintion.
			int nextDayNum = m_simulationController.DateTime.Day + 1;
			string dateLabel = DateLabelConstants.GetLabelForDay( nextDayNum );

			m_simulationController.AdvanceToNextDay( dateLabel );

			GameEvents.OnDayAdvanced?.Invoke( m_simulationController.DateTime.Day );

			//Auto-start next day of content
			TriggerDayIntroDialogue( nextDayNum );



		}

		private void TriggerDayIntroDialogue(int dayNum)
		{
			string storyletName = DateLabelConstants.GetStoryletForDayStart( dayNum );
			if ( storyletName == null )
			{
				throw new Exception( "Invalid day/storylet provided for Day Start!" );
			}
			else
			{
				Storylet dayStartStorylet = m_dialogueManager.Story.GetStorylet( storyletName );
				m_dialogueManager.RunStorylet( dayStartStorylet );
			}

		}


		/// <summary>
		/// Get a list of all location storylets a player could execute.
		/// </summary>
		/// <returns></returns>
		public List<LocationStoryletInfo> GetEligibleLocationStorylets()
		{
			List<LocationStoryletInfo> locationInfo = new List<LocationStoryletInfo>();

			foreach ( var location in m_CurrentLocation.ConnectedLocations )
			{
				if ( !m_locationStorylets.ContainsKey( location.UniqueID ) ) continue;

				Storylet locationStorylet = m_locationStorylets[location.UniqueID];

				// Skip storylets still on cooldown
				if ( locationStorylet.CooldownTimeRemaining > 0 ) continue;

				// Skip storylets that are not repeatable
				if ( !locationStorylet.IsRepeatable && locationStorylet.TimesPlayed > 0 ) continue;

				bool hasRequiredActions = LocationHasRequiredActions( location );
				bool hasAuxiliaryActions = LocationHasAuxiliaryActions( location );

				// Query the social engine database
				if ( locationStorylet.Precondition != null )
				{
					var results = locationStorylet.Precondition.Query.Run( DB );

					if ( !results.Success ) continue;

					if ( results.Bindings.Length > 0 )
					{
						foreach ( var bindingDict in results.Bindings )
						{
							locationInfo.Add(
								new LocationStoryletInfo(
									new StoryletInstance(
										locationStorylet, bindingDict, locationStorylet.Weight )
								)
								{
									hasAuxiliaryActivities = hasAuxiliaryActions,
									hasRequiredActivities = hasRequiredActions
								}
							);
						}
					}
					else
					{
						locationInfo.Add(
							new LocationStoryletInfo(
								new StoryletInstance(
									locationStorylet,
									new Dictionary<string, object>(),
									locationStorylet.Weight
								)
							)
							{
								hasAuxiliaryActivities = hasAuxiliaryActions,
								hasRequiredActivities = hasRequiredActions
							}
						);
					}
				}
				else
				{
					locationInfo.Add(
						new LocationStoryletInfo(
							new StoryletInstance(
								locationStorylet,
								new Dictionary<string, object>(),
								locationStorylet.Weight
							)
						)
						{
							hasAuxiliaryActivities = hasAuxiliaryActions,
							hasRequiredActivities = hasRequiredActions
						}
					);
				}
			}

			return locationInfo;
		}

		public bool LocationHasAuxiliaryActions(Location location)
		{
			foreach ( var (_, actionStorylet) in m_actionStorylets )
			{
				if ( !actionStorylet.Tags.Contains( location.UniqueID ) ) continue;

				if ( !actionStorylet.Tags.Contains( "auxiliary" ) ) continue;

				// Skip storylets still on cooldown
				if ( actionStorylet.CooldownTimeRemaining > 0 ) continue;

				// Skip storylets that are not repeatable
				if ( !actionStorylet.IsRepeatable && actionStorylet.TimesPlayed > 0 ) continue;

				// Query the social engine database. If the query passes, then
				// this action still need to be taken.
				if ( actionStorylet.Precondition != null )
				{
					var results = actionStorylet.Precondition.Query.Run( DB );

					if ( results.Success )
					{
						return true;
					}
				}
				else
				{
					return true;
				}
			}

			return false;
		}

		public bool LocationHasRequiredActions(Location location)
		{
			foreach ( var (_, actionStorylet) in m_actionStorylets )
			{
				if ( !actionStorylet.Tags.Contains( location.UniqueID ) ) continue;

				if ( !actionStorylet.Tags.Contains( "required" ) ) continue;

				// Skip storylets still on cooldown
				if ( actionStorylet.CooldownTimeRemaining > 0 ) continue;

				// Skip storylets that are not repeatable
				if ( !actionStorylet.IsRepeatable && actionStorylet.TimesPlayed > 0 ) continue;

				// Query the social engine database. If the query passes, then
				// this action still need to be taken.
				if ( actionStorylet.Precondition != null )
				{
					var results = actionStorylet.Precondition.Query.Run( DB );

					if ( results.Success )
					{
						return true;
					}
				}
				else
				{
					return true;
				}
			}

			return false;
		}

		public bool ExistsUnseenRequiredContentForDay()
		{
			foreach ( var (_, actionStorylet) in m_actionStorylets )
			{
				if ( !actionStorylet.Tags.Contains( "required" ) ) continue;

				// Skip storylets still on cooldown
				if ( actionStorylet.CooldownTimeRemaining > 0 ) continue;

				// Skip storylets that are not repeatable
				if ( !actionStorylet.IsRepeatable && actionStorylet.TimesPlayed > 0 ) continue;

				// Query the social engine database. If the query passes, then
				// this action still need to be taken.
				if ( actionStorylet.Precondition != null )
				{
					var results = actionStorylet.Precondition.Query.Run( DB );

					if ( results.Success )
					{
						return true;
					}
				}
				else
				{
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Get a list of all action storylets a player could execute.
		/// </summary>
		/// <returns></returns>
		public List<ActionStoryletInfo> GetEligibleActionStorylets(Location location)
		{
			List<ActionStoryletInfo> actionInfo = new List<ActionStoryletInfo>();

			foreach ( var (uid, storylet) in m_actionStorylets )
			{
				if ( !storylet.Tags.Contains( location.UniqueID ) ) continue;

				// Skip storylets still on cooldown
				if ( storylet.CooldownTimeRemaining > 0 ) continue;

				// Skip storylets that are not repeatable
				if ( !storylet.IsRepeatable && storylet.TimesPlayed > 0 ) continue;

				bool isRequired = storylet.Tags.Contains( "required" );
				bool isAuxiliary = storylet.Tags.Contains( "auxiliary" );

				// Query the social engine database
				if ( storylet.Precondition != null )
				{
					var results = storylet.Precondition.Query.Run( DB );

					if ( !results.Success ) continue;

					if ( results.Bindings.Length > 0 )
					{
						foreach ( var bindingDict in results.Bindings )
						{
							actionInfo.Add(
								new ActionStoryletInfo(
									new StoryletInstance( storylet, bindingDict, storylet.Weight )
								)
								{
									isAuxiliaryAction = isAuxiliary,
									isRequiredAction = isRequired
								}
							);
						}
					}
					else
					{
						actionInfo.Add(
							new ActionStoryletInfo(
								new StoryletInstance(
									storylet, new Dictionary<string, object>(), storylet.Weight )
							)
							{
								isAuxiliaryAction = isAuxiliary,
								isRequiredAction = isRequired
							}
						);
					}
				}
				else
				{
					actionInfo.Add(
						new ActionStoryletInfo(
							new StoryletInstance(
								storylet, new Dictionary<string, object>(), storylet.Weight )
						)
						{
							isAuxiliaryAction = isAuxiliary,
							isRequiredAction = isRequired
						}
					);
				}
			}

			return actionInfo;
		}

		public void NavigateToMainMenu()
		{
			DataPersistenceManager.ClearSaveData();
			SceneManager.LoadScene( "Scenes/MainMenu" );
		}

		private void RegisterExternalInkFunctions(Ink.Runtime.Story story)
		{
			story.BindExternalFunction(
				"SetPlayerLocation",
				(string locationID) =>
				{
					this.SetPlayerLocation( locationID );
				}
			);

			story.BindExternalFunction(
				"GetOpinion",
				(string subject, string target) =>
				{
					return this.m_socialEngine.State
						.GetRelationship( subject, target )
						.Stats.GetStat( "Opinion" ).Value;
				}
			);

			story.BindExternalFunction(
				"SetCurrentLocation",
				(string locationId) =>
				{
					Location location = m_simulationController.GetLocation( locationId );

					if ( m_CurrentLocation != location )
					{
						m_CurrentLocation = location;
						DB.Insert( $"currentLocation!{locationId}" );
						m_dialogueManager.SetBackground(
							new BackgroundInfo(
								locationId,
								new string[0]
							)
						);
					}
				}
			);

			story.BindExternalFunction(
				"HasUnseenAuxiliaryActions",
				() =>
				{
					foreach ( var (_, actionStorylet) in m_actionStorylets )
					{
						if ( !actionStorylet.Tags.Contains( "auxiliary" ) ) continue;

						// Skip storylets still on cooldown
						if ( actionStorylet.CooldownTimeRemaining > 0 ) continue;

						// Skip storylets that are not repeatable
						if ( !actionStorylet.IsRepeatable && actionStorylet.TimesPlayed > 0 ) continue;

						// Query the social engine database. If the query passes, then
						// this action still need to be taken.
						if ( actionStorylet.Precondition != null )
						{
							var results = actionStorylet.Precondition.Query.Run( DB );

							if ( results.Success )
							{
								return true;
							}
						}
						else
						{
							return true;
						}
					}

					return false;
				}
			);

			story.BindExternalFunction(
				"HasUnseenRequiredActions",
				() => { return ExistsUnseenRequiredContentForDay(); }
			);

			story.BindExternalFunction(
				"FadeToBlack",
				// Fade the screen to black after a given delay
				(float delaySeconds) =>
				{
					GameEvents.OnFadeToBlack( delaySeconds );
				}
			);

			story.BindExternalFunction(
				"FadeFromBlack",
				// Fade the screen from black after a given delay
				(float delaySeconds) =>
				{
					GameEvents.OnFadeFromBlack( delaySeconds );
				}
			);

			story.BindExternalFunction(
				"ShowInfoDialog",
				(string dialogId) =>
				{
					GameEvents.InfoDialogShown?.Invoke( dialogId );
				}
			);

			story.BindExternalFunction(
				"AdvanceDay",
				() =>
				{
					m_simulationController.AdvanceToNextDay();
				}
			);

			story.BindExternalFunction(
				"LockLocations",
				(string locationIds, string message) =>
				{
					string[] locationIdList = locationIds.Split( "," ).Select( s => s.Trim() ).ToArray();

					foreach ( string id in locationIdList )
					{
						m_simulationController.GetLocation( id ).LockLocation( message );
					}
				}
			);

			story.BindExternalFunction(
				"UnlockLocations",
				(string locationIds) =>
				{
					string[] locationIdList = locationIds.Split( "," ).Select( s => s.Trim() ).ToArray();

					foreach ( string id in locationIdList )
					{
						m_simulationController.GetLocation( id ).UnlockLocation();
					}
				}
			);

			story.BindExternalFunction(
				"LockAllLocations",
				(string message) =>
				{
					foreach ( Location location in m_simulationController.Locations )
					{
						if ( location == m_Player.Location ) continue;

						location.LockLocation( message );
					}
				}
			);

			story.BindExternalFunction(
				"UnlockAllLocations",
				() =>
				{
					foreach ( Location location in m_simulationController.Locations )
					{
						location.UnlockLocation();
					}
				}
			);

			story.BindExternalFunction(
				"EnableLocationButton",
				() =>
				{
					EnableLocationButton();
				}
			);

			story.BindExternalFunction(
				"DisableLocationButton",
				() =>
				{
					DisableLocationButton();
				}
			);
		}

		public void DisableLocationButton()
		{
			m_LocationButton.interactable = false;
		}

		public void EnableLocationButton()
		{
			m_LocationButton.interactable = true;
		}

		public void DisableActionButton()
		{
			m_ActionButton.interactable = false;
		}

		public void EnableActionButton()
		{
			m_ActionButton.interactable = true;
		}

		private void OnActionSelectModalShown()
		{
			//Disable button, show blank dialogue if no action storylets exist
			List<ActionStoryletInfo> storylets = GetEligibleActionStorylets( m_CurrentLocation );
			if ( storylets.Count > 0 )
			{
				EnableActionButton();
			}
			GameEvents.AvailableActionsUpdated?.Invoke( storylets );

		}

		//TODO: Clunky - ideally we'd have an event trigger to signify the end of a storylet and/or
		//have this update only when the change action/location menu is shown. Current 
		//architecture is to only update on opening/closing window, which causes problematic UI
		//patterns.
		private void UpdateActionButton()
		{
			List<ActionStoryletInfo> storylets = GetEligibleActionStorylets( m_CurrentLocation );
			if ( storylets.Count <= 0 )
			{
				DisableActionButton();
			}
		}

		private void OnLocationSelectModalShown()
		{
			GameEvents.AvailableLocationsUpdated?.Invoke( GetEligibleLocationStorylets() );

		}

		private void OnActionSelected(StoryletInstance action)
		{
			m_dialogueManager.RunStoryletInstance( action );
			UpdateActionButton();
		}

		public void EnableAutoSave()
		{
			m_AutoSaveEnabled = true;
			GameEvents.OnSceneEnd += AutoSave;
		}

		public void DisableAutoSave()
		{
			m_AutoSaveEnabled = false;
			GameEvents.OnSceneEnd -= AutoSave;
		}

		public void AutoSave()
		{
			SaveGame( true );
		}

		private string[] SerializeDatabase()
		{
			if ( m_simulationController == null || m_simulationController.DB == null )
			{
				return new string[0];
			}

			var nodeStack = new Stack<INode>( m_simulationController.DB.Root.Children );
			var entryList = new List<string>();

			while ( nodeStack.Count > 0 )
			{
				INode node = nodeStack.Pop();

				IEnumerable<INode> children = node.Children;

				if ( children.Count() > 0 )
				{
					// Add children to the stack
					foreach ( var child in children )
					{
						nodeStack.Push( child );
					}
				}
				else
				{
					// This is a leaf
					entryList.Add( node.GetPath() );
				}
			}

			return entryList.ToArray();
		}

		//Overloaded save game for default calls from button listeners.
		public void SaveGame()
		{
			SaveGame( m_AutoSaveEnabled );
		}

		public void SaveGame(bool isAutoSave)
		{
			GameState gameState = GameStateManager.GetGameState();


			SaveData saveData = new SaveData();

			if ( DataPersistenceManager.SaveData != null )
			{
				saveData = DataPersistenceManager.SaveData;
			}

			saveData.levelId = gameState.levelId;
			saveData.currentDay = m_simulationController.DateTime.Day;
			saveData.currentTimeOfDay = m_simulationController.DateTime.TimeOfDay.ToString();
			//TODO: Hacky, should probably fix this by adding a "purgatory" location. For now, if a character hasn't been put in a spot yet, we'll slap undefined on there.
			if ( m_Player.Location )
			{
				saveData.currentLocationId = m_Player.Location.UniqueID;
			}
			else
			{
				saveData.currentLocationId = "Undefined";
			}


			saveData.isAutoSave = isAutoSave;
			saveData.totalPlaytime = (int)PlayTimeTracker.Instance.GetTotalPlayTime();
			saveData.databaseEntries = SerializeDatabase();
			saveData.playerId = gameState.PlayerId.ToString();
			saveData.dialogueHistory = gameState.DialogueHistory.Select( (h) =>
			{
				return new DialogueHistoryEntryData()
				{
					speakerId = h.Speaker,
					text = h.Text,
				};
			} ).ToArray();
			saveData.storyData = new StoryData()
			{
				inkJson = m_dialogueManager.Story.InkStory.state.ToJson(),
				storylets = m_dialogueManager.Story.Storylets.Select( (s) =>
				{
					return new StoryletData()
					{
						cooldown = s.CooldownTimeRemaining,
						storyletId = s.ID,
						timesVisited = (int)s.TimesPlayed,
					};
				} ).ToArray()
			};
			saveData.socialEngineJson = new TdrsJsonExporter().Export(
				m_socialEngine.State
			);
			saveData.dilemmas = DilemmaSystem.Instance.SerializeDilemmas().ToArray();

			DataPersistenceManager.SaveGame( saveData );

			//We're hacking badly here - set save game state button to default.
			EventSystem.current.SetSelectedGameObject(null);

		}

		private void OnGameSaved(GameSavedEventResult result)
		{
			if ( result.success )
			{
				NotificationManager.Instance.QueueNotification( "Game Saved!" );
				AudioManager.PlayNotificationSound();
			}
			else
			{
				NotificationManager.Instance.QueueNotification( "Save Failed: " + result.message );
				Debug.LogError( "Save Failed: " + result.message );
			}
		}
	}
}
