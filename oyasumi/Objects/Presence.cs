﻿using System;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using oyasumi.Database;
using oyasumi.Database.Models;
using oyasumi.Enums;
using oyasumi.IO;
using oyasumi.Layouts;
using oyasumi.Utilities;
using System.Linq;

namespace oyasumi.Objects
{
	public class Presence
	{
		public readonly string Token;
		public readonly string Username;

		public readonly User User;
		public readonly UserStats UserStats;
		public readonly OyasumiDbContext DbContext;

		public readonly int Id;
		public readonly int LoginTime = Time.CurrentUnixTimestamp;
		public Privileges Privileges;

		// --- User Presence
		public byte Timezone;
		public float Longitude;
		public float Latitude;
		public byte CountryCode = 1;
		// --- > Shared
		public int Rank = 1;
		// --- User Stats
		public PresenceStatus Status;
		public long RankedScore;
        public float Accuracy;
		public int PlayCount;
		public long TotalScore;
		public short Performance;
		// --

		public int LastPing;

        private readonly ConcurrentQueue<Packet> _packetQueue = new ConcurrentQueue<Packet>();

		public Presence(int id, string username)
		{
			Id = id;
			Username = username;
			Token = Guid.NewGuid().ToString();
			Privileges = Privileges.Normal | Privileges.Verified;
			Status = new PresenceStatus
			{
				Status = ActionStatuses.Unknown,
				StatusText = "",
				BeatmapChecksum = "",
				BeatmapId = 0,
				CurrentMods = Mods.None,
				CurrentPlayMode = PlayMode.Osu
			};
			User = Base.UserCache[username];

			// TODO: remake this? why? because it gets any free context 
			// and then it will probably used at least one time in 
			// multiple presences. Only thoughts.
			DbContext = OyasumiDbContextFactory.Get(); 

			UserStats = DbContext.UsersStats.FirstOrDefault(x => x.Id == Id);

			UpdateUserStats();
			this.UserStats();
		}

		public async void UpdateUserStats()
		{
			var context = OyasumiDbContextFactory.Get();
			var stats = await context.UsersStats.FirstOrDefaultAsync(x => x.Id == Id);

			var performance = Status.CurrentPlayMode switch
			{
				PlayMode.Osu => stats.PerformanceOsu,
				PlayMode.Taiko => stats.PerformanceTaiko,
				PlayMode.CatchTheBeat => stats.PerformanceCtb,
				PlayMode.OsuMania => stats.PerformanceMania,
				_ => 0
			};

			if (performance > short.MaxValue)
				performance = 0;

			var totalScore = Status.CurrentPlayMode switch
			{
				PlayMode.Osu => stats.TotalScoreOsu,
				PlayMode.Taiko => stats.TotalScoreTaiko,
				PlayMode.CatchTheBeat => stats.TotalScoreCtb,
				PlayMode.OsuMania => stats.TotalScoreMania,
				_ => 0
			};

			var rankedScore = Status.CurrentPlayMode switch
			{
				PlayMode.Osu => stats.RankedScoreOsu,
				PlayMode.Taiko => stats.RankedScoreTaiko,
				PlayMode.CatchTheBeat => stats.RankedScoreCtb,
				PlayMode.OsuMania => stats.RankedScoreMania,
				_ => 0
			};

			var accuracy = Status.CurrentPlayMode switch
			{
				PlayMode.Osu => stats.AccuracyOsu * 100,
				PlayMode.Taiko => stats.AccuracyTaiko * 100,
				PlayMode.CatchTheBeat => stats.AccuracyCtb * 100,
				PlayMode.OsuMania => stats.AccuracyMania * 100,
				_ => 0
			};

			var playCount = Status.CurrentPlayMode switch
			{
				PlayMode.Osu => stats.PlaycountOsu,
				PlayMode.Taiko => stats.PlaycountTaiko,
				PlayMode.CatchTheBeat => stats.PlaycountCtb,
				PlayMode.OsuMania => stats.PlaycountMania,
				_ => 0
			};

			RankedScore = rankedScore;
			Accuracy = accuracy;
			PlayCount = playCount;
			TotalScore = totalScore;
			Performance = (short)performance;
		}

		public async void AddScore(long score, bool ranked, PlayMode mode)
		{
			var context = OyasumiDbContextFactory.Get();
			var stats = await context.UsersStats.FirstOrDefaultAsync(x => x.Id == Id);
			switch (mode)
			{
				case PlayMode.Osu:
					if (ranked)
						stats.RankedScoreOsu += score;
					else
						stats.TotalScoreOsu += score;

					break;

				case PlayMode.Taiko:
					if (ranked)
						stats.RankedScoreTaiko += score;
					else
						stats.TotalScoreTaiko += score;

					break;

				case PlayMode.CatchTheBeat:
					if (ranked)
						stats.RankedScoreCtb += score;
					else
						stats.TotalScoreCtb += score;

					break;

				case PlayMode.OsuMania:
					if (ranked)
						stats.RankedScoreMania += score;
					else
						stats.TotalScoreMania += score;

					break;
			}
			await context.SaveChangesAsync();
		}

		public async void AddPlaycount(PlayMode mode)
		{
			var context = OyasumiDbContextFactory.Get();
			var stats = await context.UsersStats.FirstOrDefaultAsync(x => x.Id == Id);
			switch (mode)
			{
				case PlayMode.Osu:
					stats.PlaycountOsu++;
					break;

				case PlayMode.Taiko:
					stats.PlaycountTaiko++;
					break;

				case PlayMode.CatchTheBeat:
					stats.PlaycountCtb++;
					break;

				case PlayMode.OsuMania:
					stats.PlaycountMania++;
					break;
			}
			await context.SaveChangesAsync();
		}


		// taken from Sora https://github.com/Chimu-moe/Sora/blob/7bba59c8000b440f7f81d2a487a5109590e37068/src/Sora/Database/Models/DBLeaderboard.cs#L200
		public async Task<double> UpdateAccuracy(PlayMode mode)
		{
			var context = OyasumiDbContextFactory.Get();
			var totalAcc = 0d;
			var divideTotal = 0d;
			var i = 0;

			var scores = (await context
				.Scores
				.ToListAsync())
				.Where(s => s.PlayMode == mode)
				.Where(s => s.UserId == Id)
				.Take(500)
				.OrderByDescending(s => s.PerformancePoints);

			foreach (var s in scores)
			{
				var divide = Math.Pow(.95d, i);

				totalAcc += s.Accuracy * divide;
				divideTotal += divide;

				i++;
			}

			var acc = divideTotal > 0 ? totalAcc / divideTotal : 0;

			Accuracy = (float)acc; // Keep accuracy up to date;

			return acc;
		}

		public void PacketEnqueue(Packet p)
		{
			_packetQueue.Enqueue(p);
		}
		
		public async Task<byte[]> WritePackets()
		{
			var writer = new PacketWriter();
			while (_packetQueue.TryDequeue(out var p))
			{
				await writer.Write(p);
			}

			return writer.ToBytes();
		}
	}
}
