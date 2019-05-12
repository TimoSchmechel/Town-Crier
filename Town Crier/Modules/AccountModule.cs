﻿using Discord.Addons.Interactive;
using Discord.Commands;
using Discord.WebSocket;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using TownCrier.Database;
using TownCrier.Services;

namespace TownCrier
{
	[Group("account")]
	public class AccountModule : InteractiveBase<SocketCommandContext>
	{
		public AltaAPI AltaApi { get; set; }
		public TownDatabase Database { get; set; }

		public class AccountDatabase
		{
			public Dictionary<ulong, AccountInfo> accounts = new Dictionary<ulong, AccountInfo>();

			public Dictionary<int, ulong> altaIdMap = new Dictionary<int, ulong>();

			public SortedSet<AccountInfo> expiryAccounts = new SortedSet<AccountInfo>(new AccountInfo.Comparer());
		}

		public class AccountInfo
		{
			public class Comparer : IComparer<AccountInfo>
			{
				public int Compare(AccountInfo x, AccountInfo y)
				{
					return x.supporterExpiry.CompareTo(y.supporterExpiry);
				}
			}

			public ulong discordIdentifier;
			public int altaIdentifier;
			public DateTime supporterExpiry;
			public bool isSupporter;
			public string username;
		}

		class VerifyData
		{
			public string discord;
		}

		static SocketGuild guild;
		static SocketRole supporterRole;
		static SocketTextChannel supporterChannel;
		static SocketTextChannel generalChannel;

		// NOTE: Both of these commands will be tied to a global clock that will periodically update all accounts every 15~30 mins.

		//[Command("update")]
		//public async Task Update()
		//{
		//	if (database.accounts.TryGetValue(Context.User.Id, out AccountInfo info))
		//	{
		//		await UpdateAsync(info, (SocketGuildUser)Context.User);

		//		await ReplyAsync(Context.User.Mention + ", " + $"Hey {info.username}, your account info has been updated!");
		//	}
		//	else
		//	{
		//		await ReplyAsync(Context.User.Mention + ", " + "You have not linked to an Alta account! To link, visit the 'Account Settings' page in the launcher.");
		//	}
		//}


		//[Command("forceupdate")]
		//public async Task Update(SocketUser user)
		//{
		//	if (database.accounts.TryGetValue(user.Id, out AccountInfo info))
		//	{
		//		await UpdateAsync(info, null);

		//		await ReplyAsync(Context.User.Mention + ", " + $"{info.username}'s account info has been updated!");
		//	}
		//	else
		//	{
		//		await ReplyAsync(Context.User.Mention + ", " + user.Username + " have not linked to an Alta account!");
		//	}
		//}


		[Command("unlink")]
		public async Task Unlink()
		{
			var user = Database.GetUser(Context.User);

			if (user.AltaInfo != null && user.AltaInfo.Identifier != 0)
			{
				user.AltaInfo.Unlink();

				await ReplyAsync(Context.User.Mention + ", " + "You are no longer linked to an Alta account!");
			}
			else
			{
				await ReplyAsync(Context.User.Mention + ", " + "You have not linked to an Alta account! To link, visit the 'Account Settings' page in the launcher.");
			}
		}

		[Command("IsLinked"), Alias("Linked")]
		public async Task IsLinked()
		{
			TownUser user = Database.GetUser(Context.User);

			if (user.AltaInfo == null || user.AltaInfo.Identifier == 0)
			{
				await ReplyAsync(Context.User.Mention + ", " + "You have not linked to an Alta account! To link, visit the 'Account Settings' page in the launcher.");
			}
			else
			{
				await ReplyAsync(Context.User.Mention + ", " + $"Your account is currently linkedto " + user.AltaInfo.Username + "!");
			}
		}
		
		[Command("Verify")]
		public async Task Verify([Remainder]string encoded)
		{
			JwtSecurityToken token;
			Claim userData;
			Claim altaId;

			TownUser user = Database.GetUser(Context.User);

			try
			{
				token = new JwtSecurityToken(encoded);

				userData = token.Claims.FirstOrDefault(item => item.Type == "user_data");
				altaId = token.Claims.FirstOrDefault(item => item.Type == "UserId");
			}
			catch
			{
				await ReplyAsync(Context.User.Mention + ", " + "Invalid verification token.");
				return;
			}

			if (userData == null || altaId == null)
			{
				await ReplyAsync(Context.User.Mention + ", " + "Invalid verification token.");
			}
			else
			{
				try
				{
					VerifyData result = JsonConvert.DeserializeObject<VerifyData>(userData.Value);

					string test = result.discord.ToLower();
					string expected = Context.User.Username.ToLower() + "#" + Context.User.Discriminator;
					string alternate = Context.User.Username.ToLower() + " #" + Context.User.Discriminator;


					if (test != expected.ToLower() && test != alternate.ToLower())
					{
						await ReplyAsync(Context.User.Mention + ", " + "Make sure you correctly entered your account info! You entered: " + result.discord + ". Expected: " + expected);
						return;
					}

					int id = int.Parse(altaId.Value);

					bool isValid = await AltaApi.ApiClient.ServicesClient.IsValidShortLivedIdentityTokenAsync(token);

					if (isValid)
					{
						if (user.AltaInfo == null)
						{
							user.AltaInfo = new UserAltaInfo();
						}

						if (user.AltaInfo.Identifier == id)
						{
							await ReplyAsync(Context.User.Mention + ", " + "Already connected!");
							return;
						}

						if (user.AltaInfo.Identifier != 0)
						{
							await ReplyAsync(Context.User.Mention + ", " + $"Unlinking your Discord from {user.AltaInfo.Username}...");

							user.AltaInfo.Unlink();
						}

						if (Database.Users.Exists(x => x.AltaInfo.Identifier != 0 && x.AltaInfo.Identifier == id && x.UserId != Context.User.Id))
						{
							var oldUsers = Database.Users.Find(x => x.AltaInfo.Identifier == id && x.UserId != Context.User.Id);

							foreach (var x in oldUsers)
							{
								var olddiscorduser = Context.Client.GetUser(x.UserId);

								await ReplyAsync(Context.User.Mention + ", " + $"Unlinking your Alta account from {olddiscorduser.Mention}...");

								x.AltaInfo.Unlink();
							}
						}

						user.AltaInfo.Identifier = id;
						user.AltaInfo.Username = AltaApi.ApiClient.UserClient.GetUserInfoAsync(id).GetAwaiter().GetResult().Username;

						await ReplyAsync(Context.User.Mention + ", " + $"Successfully linked to your Alta account! Hey there {user.AltaInfo.Username}!");
					}
					else
					{
						await ReplyAsync(Context.User.Mention + ", " + "Invalid token! Try creating a new one!");
					}
				}
				catch (Exception e)
				{
					await ReplyAsync(Context.User.Mention + ", " + "Invalid verification token : " + e.Message);
				}
			}
		}
	}
}