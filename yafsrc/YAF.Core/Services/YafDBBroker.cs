/* Yet Another Forum.net
 * Copyright (C) 2006-2012 Jaben Cargman
 * http://www.yetanotherforum.net/
 * 
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation; either version 2
 * of the License, or (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, write to the Free Software
 * Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
 */

namespace YAF.Core.Services
{
    #region Using

    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Web;

    using YAF.Classes;
    using YAF.Classes.Data;
    using YAF.Types;
    using YAF.Types.Constants;
    using YAF.Types.Flags;
    using YAF.Types.Interfaces;
    using YAF.Types.Objects;
    using YAF.Utils;
    using YAF.Utils.Helpers;

    #endregion

    /// <summary>
    ///     Class used for multi-step DB operations so they can be cached, etc.
    /// </summary>
    public class YafDBBroker : IDBBroker, IHaveServiceLocator
    {
        #region Constructors and Destructors

        /// <summary>
        ///     Initializes a new instance of the <see cref="YafDBBroker" /> class.
        /// </summary>
        /// <param name="serviceLocator"> The service locator. </param>
        /// <param name="httpSessionState"> The http session state. </param>
        /// <param name="dataCache"> The data cache. </param>
        public YafDBBroker(
            IServiceLocator serviceLocator,
            YafBoardSettings boardSettings,
            HttpSessionStateBase httpSessionState,
            IDataCache dataCache,
            IDbFunction dbFunction)
        {
            this.ServiceLocator = serviceLocator;
            this.BoardSettings = boardSettings;
            this.HttpSessionState = httpSessionState;
            this.DataCache = dataCache;
            this.DbFunction = dbFunction;
        }

        #endregion

        #region Public Properties

        public YafBoardSettings BoardSettings { get; set; }

        /// <summary>
        ///     Gets or sets DataCache.
        /// </summary>
        public IDataCache DataCache { get; set; }

        public IDbFunction DbFunction { get; set; }

        /// <summary>
        ///     Gets or sets HttpSessionState.
        /// </summary>
        public HttpSessionStateBase HttpSessionState { get; set; }

        /// <summary>
        ///     Gets or sets ServiceLocator.
        /// </summary>
        public IServiceLocator ServiceLocator { get; set; }

        #endregion

        #region Public Methods and Operators

        /// <summary>
        ///     The user lazy data.
        /// </summary>
        /// <param name="userId"> The user ID. </param>
        /// <returns> Returns the Active User </returns>
        public DataRow ActiveUserLazyData(int userId)
        {
            // get a row with user lazy data...
            return
                this.DataCache.GetOrSet(
                    Constants.Cache.ActiveUserLazyData.FormatWith(userId),
                    () =>
                    LegacyDb.user_lazydata(
                        userId,
                        YafContext.Current.PageBoardID,
                        this.BoardSettings.AllowEmailSending,
                        this.BoardSettings.EnableBuddyList,
                        this.BoardSettings.AllowPrivateMessages,
                        this.BoardSettings.EnableAlbum,
                        this.BoardSettings.UseStyledNicks).Table,
                    TimeSpan.FromMinutes(this.BoardSettings.ActiveUserLazyDataCacheTimeout)).Rows[0];
        }

        /// <summary>
        ///     Adds the Thanks info to a dataTable
        /// </summary>
        /// <param name="dataRows"> The data Rows. </param>
        public void AddThanksInfo(IEnumerable<DataRow> dataRows)
        {
            var messageIds = dataRows.Select(x => x.Field<int>("MessageID"));

            // Initialize the "IsthankedByUser" column.
            dataRows.ForEach(x => x["IsThankedByUser"] = false);

            // Initialize the "Thank Info" column.
            dataRows.ForEach(x => x["ThanksInfo"] = string.Empty);

            // Iterate through all the thanks relating to this topic and make appropriate
            // changes in columns.
            var allThanks = LegacyDb.MessageGetAllThanks(messageIds.ToDelimitedString(",")).ToList();

            foreach (var f in
                allThanks.Where(t => t.FromUserID != null && t.FromUserID == YafContext.Current.PageUserID).SelectMany(
                    thanks => dataRows.Where(x => x.Field<int>("MessageID") == thanks.MessageID)))
            {
                f["IsThankedByUser"] = "true";
                f.AcceptChanges();
            }

            var thanksFieldNames = new[] { "ThanksFromUserNumber", "ThanksToUserNumber", "ThanksToUserPostsNumber" };

            foreach (DataRow postRow in dataRows)
            {
                var messageId = postRow.Field<int>("MessageID");

                postRow["MessageThanksNumber"] =
                    allThanks.Count(t => t.FromUserID != null && t.MessageID == messageId);

                var thanksFiltered = allThanks.Where(t => t.MessageID == messageId);

                if (thanksFiltered.Any())
                {
                    var thanksItem = thanksFiltered.First();

                    postRow["ThanksFromUserNumber"] = thanksItem.ThanksFromUserNumber ?? 0;
                    postRow["ThanksToUserNumber"] = thanksItem.ThanksToUserNumber ?? 0;
                    postRow["ThanksToUserPostsNumber"] = thanksItem.ThanksToUserPostsNumber ?? 0;
                }
                else
                {
                    DataRow row = postRow;
                    thanksFieldNames.ForEach(f => row[f] = 0);
                }

                // load all all thanks info into a special column...
                postRow["ThanksInfo"] = thanksFiltered
                    .Where(t => t.FromUserID != null)
                    .Select(x => "{0}|{1}".FormatWith(x.FromUserID.Value, x.ThanksDate))
                    .ToDelimitedString(",");

                postRow.AcceptChanges();
            }
        }

        /// <summary>
        ///     Returns the layout of the board
        /// </summary>
        /// <param name="boardID"> The board ID. </param>
        /// <param name="userID"> The user ID. </param>
        /// <param name="categoryID"> The category ID. </param>
        /// <param name="parentID"> The parent ID. </param>
        /// <returns> The board layout. </returns>
        public DataSet BoardLayout(int boardID, int userID, int? categoryID, int? parentID)
        {
            if (categoryID.HasValue && categoryID == 0)
            {
                categoryID = null;
            }

            using (var ds = new DataSet())
            {
                // get the cached version of forum moderators if it's valid
                var moderator = this.DataCache.GetOrSet(
                    Constants.Cache.ForumModerators,
                    this.GetModerators,
                    TimeSpan.FromMinutes(this.BoardSettings.BoardModeratorsCacheTimeout));

                // insert it into this DataSet
                ds.Tables.Add(moderator.Copy());

                // get the Category Table
                DataTable category = this.DataCache.GetOrSet(
                    Constants.Cache.ForumCategory,
                    () =>
                        {
                            var catDt = this.DbFunction.GetAsDataTable(cdb => cdb.category_list(boardID, null));
                            catDt.TableName = "Category";
                            return catDt;
                        },
                    TimeSpan.FromMinutes(this.BoardSettings.BoardCategoriesCacheTimeout));

                // add it to this dataset
                ds.Tables.Add(category.Copy());

                DataTable categoryTable = ds.Tables["Category"];

                if (categoryID.HasValue)
                {
                    // make sure this only has the category desired in the dataset
                    foreach (DataRow row in categoryTable
                        .AsEnumerable()
                        .Where(row => row.Field<int>("CategoryID") != categoryID))
                    {
                        // delete it...
                        row.Delete();
                    }

                    categoryTable.AcceptChanges();
                }

                DataTable forum = LegacyDb.forum_listread(
                    boardID,
                    userID,
                    categoryID,
                    parentID,
                    this.BoardSettings.UseStyledNicks,
                    this.BoardSettings.UseReadTrackingByDatabase);

                forum.TableName = "Forum";
                ds.Tables.Add(forum.Copy());

                ds.Relations.Add(
                    "FK_Forum_Category",
                    categoryTable.Columns["CategoryID"],
                    ds.Tables["Forum"].Columns["CategoryID"],
                    false);

                ds.Relations.Add(
                    "FK_Moderator_Forum",
                    ds.Tables["Forum"].Columns["ForumID"],
                    ds.Tables["Moderator"].Columns["ForumID"],
                    false);

                bool deletedCategory = false;

                // remove empty categories...
                foreach (DataRow row in categoryTable
                    .SelectTypedList(row => new { row, childRows = row.GetChildRows("FK_Forum_Category") })
                    .Where(@t => !@t.childRows.Any())
                    .Select(@t => @t.row))
                {
                    // remove this category...
                    row.Delete();
                    deletedCategory = true;
                }

                if (deletedCategory)
                {
                    categoryTable.AcceptChanges();
                }

                return ds;
            }
        }

        /// <summary>
        ///     The favorite topic list.
        /// </summary>
        /// <param name="userID"> The user ID. </param>
        /// <returns> Returns The favorite topic list. </returns>
        public List<int> FavoriteTopicList(int userID)
        {
            string key = this.Get<ITreatCacheKey>().Treat(Constants.Cache.FavoriteTopicList.FormatWith(userID));

            // stored in the user session...
            var favoriteTopicList = this.HttpSessionState[key] as List<int>;

            // was it in the cache?
            if (favoriteTopicList == null)
            {
                // get fresh values
                DataTable favoriteTopicListDt = this.DbFunction.GetAsDataTable(o => o.topic_favorite_list(userID));

                // convert to list...
                favoriteTopicList = favoriteTopicListDt.GetColumnAsList<int>("TopicID");

                // store it in the user session...
                this.HttpSessionState.Add(key, favoriteTopicList);
            }

            return favoriteTopicList;
        }

        /// <summary>
        ///     The get active list.
        /// </summary>
        /// <param name="guests"> The guests. </param>
        /// <param name="crawlers"> The bots. </param>
        /// <returns> Returns the active list. </returns>
        public DataTable GetActiveList(bool guests, bool crawlers)
        {
            return this.GetActiveList(this.BoardSettings.ActiveListTime, guests, crawlers);
        }

        /// <summary>
        ///     The get active list.
        /// </summary>
        /// <param name="activeTime"> The active time. </param>
        /// <param name="guests"> The guests. </param>
        /// <param name="crawlers"> The crawlers. </param>
        /// <returns> Returns the active list. </returns>
        public DataTable GetActiveList(int activeTime, bool guests, bool crawlers)
        {
            return
                this.DbFunction.GetAsDataTable(
                    cdb =>
                    cdb.active_list(
                        YafContext.Current.PageBoardID, guests, crawlers, activeTime, this.BoardSettings.UseStyledNicks, DateTime.UtcNow));
        }

        /// <summary>
        ///     The get all moderators.
        /// </summary>
        /// <returns> Returns List with all moderators </returns>
        public List<SimpleModerator> GetAllModerators()
        {
            // get the cached version of forum moderators if it's valid
            var moderator = this.DataCache.GetOrSet(
                Constants.Cache.ForumModerators,
                this.GetModerators,
                TimeSpan.FromMinutes(this.BoardSettings.BoardModeratorsCacheTimeout));

            return
                moderator.SelectTypedList(
                    row =>
                    new SimpleModerator(
                        row.Field<int>("ForumID"),
                        row.Field<string>("ForumName"),
                        row.Field<int>("ModeratorID"),
                        row.Field<string>("ModeratorName"),
                        row.Field<string>("ModeratorEmail"),
                        row.Field<string>("ModeratorAvatar"),
                        row.Field<bool>("ModeratorAvatarImage"),
                        row.Field<string>("ModeratorDisplayName"),
                        row.Field<string>("Style"),
                        row["IsGroup"].ToType<bool>())).ToList();
        }

        /// <summary>
        ///     The get custom bb code.
        /// </summary>
        /// <returns> Returns List with Custom BBCodes </returns>
        public IEnumerable<TypedBBCode> GetCustomBBCode()
        {
            return this.DataCache.GetOrSet(
                Constants.Cache.CustomBBCode, () => LegacyDb.BBCodeList(YafContext.Current.PageBoardID, null).ToList());
        }

        /// <summary>
        ///     The get latest topics.
        /// </summary>
        /// <param name="numberOfPosts"> The number of posts. </param>
        /// <returns> Returns List with Latest Topics. </returns>
        public DataTable GetLatestTopics(int numberOfPosts)
        {
            return this.GetLatestTopics(numberOfPosts, YafContext.Current.PageUserID);
        }

        /// <summary>
        ///     The get latest topics by User.
        /// </summary>
        /// <param name="numberOfPosts"> The number of posts. </param>
        /// <param name="userId"> The user id. </param>
        /// <returns> Returns List with Latest Topics. </returns>
        public DataTable GetLatestTopics(int numberOfPosts, int userId)
        {
            return this.GetLatestTopics(numberOfPosts, userId, "Style");
        }

        /// <summary>
        ///     The get latest topics.
        /// </summary>
        /// <param name="numberOfPosts"> The number of posts. </param>
        /// <param name="userId"> The user id. </param>
        /// <param name="styleColumnNames"> The style Column Names. </param>
        /// <returns> Returns List with Latest Topics. </returns>
        public DataTable GetLatestTopics(int numberOfPosts, int userId, params string[] styleColumnNames)
        {
            return
                this.StyleTransformDataTable(
                    this.DbFunction.GetAsDataTable(
                        cdb =>
                        cdb.topic_latest(
                            YafContext.Current.PageBoardID,
                            numberOfPosts,
                            userId,
                            this.BoardSettings.UseStyledNicks,
                            this.BoardSettings.NoCountForumsInActiveDiscussions,
                            this.BoardSettings.UseReadTrackingByDatabase)),
                    styleColumnNames);
        }

        /// <summary>
        ///     Get all moderators by Groups and User
        /// </summary>
        /// <returns> Returns the Moderator List </returns>
        public DataTable GetModerators()
        {
            DataTable moderator = this.DbFunction.GetAsDataTable(cdb => cdb.forum_moderators(this.BoardSettings.UseStyledNicks));
            moderator.TableName = "Moderator";

            return moderator;
        }

        /// <summary>
        ///     Get the list of recently logged in users.
        /// </summary>
        /// <param name="timeSinceLastLogin"> The time since last login in minutes. </param>
        /// <returns> The list of users in Datatable format. </returns>
        public DataTable GetRecentUsers(int timeSinceLastLogin)
        {
            return
                this.StyleTransformDataTable(
                    this.DbFunction.GetAsDataTable(
                        cdb => cdb.recent_users(YafContext.Current.PageBoardID, timeSinceLastLogin, this.BoardSettings.UseStyledNicks)));
        }

        /// <summary>
        ///     The get shout box messages.
        /// </summary>
        /// <param name="boardId"> The board id. </param>
        /// <returns> Retuns the shout box messages. </returns>
        public IEnumerable<DataRow> GetShoutBoxMessages(int boardId)
        {
            return this.DataCache.GetOrSet(
                Constants.Cache.Shoutbox,
                () =>
                    {
                        var messages =
                            this.DbFunction.GetAsDataTable(
                                cdb =>
                                cdb.shoutbox_getmessages(boardId, this.BoardSettings.ShoutboxShowMessageCount, this.BoardSettings.UseStyledNicks));
                        var flags = new MessageFlags { IsBBCode = true, IsHtml = false };

                        foreach (var row in messages.AsEnumerable())
                        {
                            string formattedMessage =
                                this.Get<IFormatMessage>().FormatMessage(row.Field<string>("Message"), flags);

                            // Extra Formating not needed already done tru this.Get<IFormatMessage>().FormatMessage
                            // formattedMessage = FormatHyperLink(formattedMessage);
                            row["Message"] = formattedMessage;
                        }

                        return messages;
                    },
                TimeSpan.FromMilliseconds(30000)).AsEnumerable();
        }

        /// <summary>
        ///     Get a simple forum/topic listing.
        /// </summary>
        /// <param name="boardId"> The board Id. </param>
        /// <param name="userId"> The user Id. </param>
        /// <param name="timeFrame"> The time Frame. </param>
        /// <param name="maxCount"> The max Count. </param>
        /// <returns> The get simple forum topic. </returns>
        public List<SimpleForum> GetSimpleForumTopic(int boardId, int userId, DateTime timeFrame, int maxCount)
        {
            var forumData = this.DbFunction
                .GetAsDataTable(cdb => cdb.forum_listall(boardId, userId))
                .SelectTypedList(x => new SimpleForum { ForumID = x.Field<int>("ForumID"), Name = x.Field<string>("Forum") })
                .ToList();

            // get topics for all forums...
            foreach (var forum in forumData)
            {
                SimpleForum forum1 = forum;

                // add topics
                var topics =
                    this.DbFunction.GetAsDataTable(
                        cdb => cdb.topic_list(forum1.ForumID, userId, timeFrame, DateTime.UtcNow, 0, maxCount, false, false, false))
                        .SelectTypedList(x => this.LoadSimpleTopic(x, forum1))
                        .Where(x => x.LastPostDate >= timeFrame)
                        .ToList();

                forum.Topics = topics;
            }

            return forumData;
        }

        /// <summary>
        ///     The get smilies.
        /// </summary>
        /// <returns> Table with list of smiles </returns>
        public IEnumerable<TypedSmileyList> GetSmilies()
        {
            return this.DataCache.GetOrSet(
                Constants.Cache.Smilies,
                () =>
                this.DbFunction.GetAsDataTable(cdb => cdb.smiley_list(YafContext.Current.PageBoardID, null))
                    .SelectTypedList(x => new TypedSmileyList(x)).ToList(),
                TimeSpan.FromMinutes(60));
        }

        /// <summary>
        ///     Loads the message text into the paged data if "Message" and
        ///     "MessageID" exists.
        /// </summary>
        /// <param name="dataRows"> The data Rows. </param>
        public void LoadMessageText(IEnumerable<DataRow> dataRows)
        {
            var messageIds =
                dataRows.AsEnumerable().Where(x => x.Field<string>("Message").IsNotSet()).Select(
                    x => x.Field<int>("MessageID"));

            var messageTextTable = this.DbFunction.GetAsDataTable(cdb => cdb.message_GetTextByIds(messageIds.ToDelimitedString(",")));

            if (messageTextTable == null)
            {
                return;
            }

            // load them into the page data...
            foreach (var dataRow in dataRows)
            {
                // find the message id in the results...
                DataRow row = dataRow;

                var message =
                    messageTextTable.AsEnumerable().FirstOrDefault(x => x.Field<int>("MessageID") == row.Field<int>("MessageID"));

                if (message == null)
                {
                    continue;
                }

                dataRow.BeginEdit();
                dataRow["Message"] = message.Field<string>("Message");
                dataRow.EndEdit();
            }
        }

        /// <summary>
        ///     The style transform func wrap.
        /// </summary>
        /// <param name="dt"> The DateTable </param>
        /// <returns> The style transform wrap. </returns>
        public DataTable StyleTransformDataTable(DataTable dt)
        {
            if (this.BoardSettings.UseStyledNicks)
            {
                var styleTransform = this.Get<IStyleTransform>();
                styleTransform.DecodeStyleByTable(dt, true);
            }

            return dt;
        }

        /// <summary>
        ///     The style transform func wrap.
        /// </summary>
        /// <param name="dt"> The DateTable </param>
        /// <param name="styleColumns"> Style columns names </param>
        /// <returns> The style transform wrap. </returns>
        public DataTable StyleTransformDataTable(DataTable dt, params string[] styleColumns)
        {
            if (this.BoardSettings.UseStyledNicks)
            {
                var styleTransform = this.Get<IStyleTransform>();
                styleTransform.DecodeStyleByTable(dt, true, styleColumns);
            }

            return dt;
        }

        /// <summary>
        ///     The Buddy list for the user with the specified UserID.
        /// </summary>
        /// <param name="userID"> The User ID. </param>
        /// <returns> The user buddy list. </returns>
        public DataTable UserBuddyList(int userID)
        {
            return this.DataCache.GetOrSet(
                Constants.Cache.UserBuddies.FormatWith(userID),
                () => this.DbFunction.GetAsDataTable(cdb => cdb.buddy_list(userID)),
                TimeSpan.FromMinutes(10));
        }

        /// <summary>
        ///     The user ignored list.
        /// </summary>
        /// <param name="userId"> The user id. </param>
        /// <returns> Returns the user ignored list. </returns>
        public List<int> UserIgnoredList(int userId)
        {
            string key = Constants.Cache.UserIgnoreList.FormatWith(userId);

            // stored in the user session...
            var userList = this.HttpSessionState[key] as List<int>;

            // was it in the cache?
            if (userList == null)
            {
                // get fresh values
                DataTable userListDt = this.DbFunction.GetAsDataTable(cdb => cdb.user_ignoredlist(userId));

                // convert to list...
                userList = userListDt.GetColumnAsList<int>("IgnoredUserID");

                // store it in the user session...
                this.HttpSessionState.Add(key, userList);
            }

            return userList;
        }

        /// <summary>
        ///     The user medals.
        /// </summary>
        /// <param name="userId"> The user id. </param>
        /// <returns> Returns the User Medals </returns>
        public DataTable UserMedals(int userId)
        {
            string key = Constants.Cache.UserMedals.FormatWith(userId);

            // get the medals cached...
            DataTable dt = this.DataCache.GetOrSet(
                key, () => this.DbFunction.GetAsDataTable(cdb => cdb.user_listmedals(userId)), TimeSpan.FromMinutes(10));

            return dt;
        }

        #endregion

        #region Methods

        /// <summary>
        ///     The load simple topic.
        /// </summary>
        /// <param name="row"> The row. </param>
        /// <param name="forum"> The forum. </param>
        /// <returns> Returns the simple topic. </returns>
        [NotNull]
        private SimpleTopic LoadSimpleTopic([NotNull] DataRow row, [NotNull] SimpleForum forum)
        {
            CodeContracts.ArgumentNotNull(row, "row");
            CodeContracts.ArgumentNotNull(forum, "forum");

            return new SimpleTopic
                {
                    TopicID = row.Field<int>("TopicID"),
                    CreatedDate = row.Field<DateTime>("Posted"),
                    Subject = row.Field<string>("Subject"),
                    StartedUserID = row.Field<int>("UserID"),
                    StartedUserName = UserMembershipHelper.GetDisplayNameFromID(row.Field<int>("UserID")),
                    Replies = row.Field<int>("Replies"),
                    LastPostDate = row.Field<DateTime>("LastPosted"),
                    LastUserID = row.Field<int>("LastUserID"),
                    LastUserName = UserMembershipHelper.GetDisplayNameFromID(row.Field<int>("LastUserID")),
                    LastMessageID = row.Field<int>("LastMessageID"),
                    FirstMessage = row.Field<string>("FirstMessage"),
                    LastMessage = LegacyDb.MessageList(row.Field<int>("LastMessageID")).First().Message,
                    Forum = forum
                };
        }

        #endregion
    }
}