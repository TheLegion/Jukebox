﻿
namespace Jukebox.Server.Services {
	using System.Collections.Generic;
	using System.Collections.Specialized;
	using System.IO;
    using System;
	using System.Linq;
	using System.ServiceModel;
	using System.ServiceModel.Web;
	using Jukebox.Server.DataProviders;
	using Jukebox.Server.Models;
	using System.Diagnostics;
    using System.ServiceModel.Channels;

	[ServiceBehavior(
		InstanceContextMode = InstanceContextMode.Single,
		ConcurrencyMode = ConcurrencyMode.Multiple)]
	class JukeboxService : IPolicyService, IPlaylistService, ISearchService, IPlayerService {
		public JukeboxService() {
			Player.Instance.TrackChanged += OnCurrentTrackChanged;
			Player.Instance.Playlist.Tracks.CollectionChanged += OnPlaylistChanged;
			Player.Instance.TrackStateChanged += new System.EventHandler<PlayerEventArgs>(OnTrackStateChanged);
		}
		
		// IPolicyService --------------------------------------------------------------------------

		public Stream GetSilverlightPolicy() {
			Debug.Print("Policy has been sent.");
			if (InstanceContext == null) InstanceContext = OperationContext.Current.InstanceContext;
			
			WebOperationContext.Current.OutgoingResponse.ContentType = "application/xml";
			return new MemoryStream(File.ReadAllBytes("clientaccesspolicy.xml"));
		}

		// ISearchService --------------------------------------------------------------------------

		public IList<Track> Search(string query) {
			return DataProviderManager.Instance.Search(query).ToList();
		}

		// IPlaylistService ------------------------------------------------------------------------

		public Playlist GetPlaylist() {
			return Player.Instance.Playlist;
		}

		public void Add(Track track) {
			Player.Instance.Playlist.Tracks.Add(track);
		}

		public void Remove(Track track) {
			Player.Instance.Playlist.Tracks.Remove(track);
		}

        /// <summary>
        /// Голоса за пропуск этой песни.
        /// </summary>
        private Dictionary<string, bool> _nextVotes = new Dictionary<string,bool>();

        /// <summary>
        /// Количество голосов, необходимое для пропуска.
        /// </summary>
        const int VOTES_TO_SKIP = 2;

        public string Next()
        {
            //var clientId = OperationContext.Current.SessionId.Split(';')[0];
            //var clientId = OperationContext.Current.SessionId;

            OperationContext context = OperationContext.Current;
            MessageProperties prop = context.IncomingMessageProperties;
            RemoteEndpointMessageProperty endpoint = prop[RemoteEndpointMessageProperty.Name] as RemoteEndpointMessageProperty;
            string clientId = endpoint.Address;


            if (Player.Instance.CurrentTrack == null)
            {
                return "Сейчас не проигрывается ни одна песня.";
            }

            if (_nextVotes.ContainsKey(clientId))
            {
                return "Вы уже голосовали против этой песни.";
            }

            _nextVotes[clientId] = true;

            int votes = _nextVotes.Count;

            if (votes == VOTES_TO_SKIP)
            {
                Player.Instance.Abort();
            }

            return string.Format("Проголосовало {0} из {1}.", votes, VOTES_TO_SKIP);
        }

		// IPlayerService --------------------------------------------------------------------------

		public Track GetCurrentTrack() {
			return Player.Instance.CurrentTrack;
		}

        public double GetVolumeLevel()
        {
            return Player.Instance.VolumeLevel;
        }

        public void SetVolumeLevel(double value)
        {
            Player.Instance.VolumeLevel = value;
        }

		private void OnCurrentTrackChanged(object sender, PlayerEventArgs e) {
            _nextVotes.Clear();
			/*foreach (IPlayerServiceCallback a in InstanceContext.IncomingChannels.Where(x => x is IPlayerServiceCallback)) {
				a.OnCurrentTrackChanged(e.Track);
			}*/
		}

		private void OnPlaylistChanged(object sender, NotifyCollectionChangedEventArgs e) {
			/*foreach (IPlaylistServiceCallback a in InstanceContext.IncomingChannels.Where(x => x is IPlaylistServiceCallback)) {
				if (e.NewItems != null) { foreach (Track track in e.NewItems) { a.OnTrackAdded(track); } }
				if (e.OldItems != null) { foreach (Track track in e.OldItems) { a.OnTrackRemoved(track); } }
			}*/
		}

		private void OnTrackStateChanged(object sender, PlayerEventArgs e) {
			/*foreach (IPlaylistServiceCallback a in InstanceContext.IncomingChannels.Where(x => x is IPlaylistServiceCallback)) {
				a.OnTrackStateChanged(e.Track);
			}*/
		}

		private InstanceContext InstanceContext { get; set; }
    }
}
