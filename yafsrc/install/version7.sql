/* Version 0.9.x */

if not exists(select * from syscolumns where id=object_id('yaf_Group') and name='IsModerator')
	alter table yaf_Group add IsModerator bit null
GO

update yaf_Group set IsModerator=0 where IsModerator is null
GO

alter table yaf_Group alter column IsModerator bit not null
GO

if not exists(select * from syscolumns where id=object_id('yaf_System') and name='AvatarUpload')
	alter table yaf_System add AvatarUpload bit null
GO

if not exists(select * from syscolumns where id=object_id('yaf_System') and name='AvatarRemote')
	alter table yaf_System add AvatarRemote bit null
GO

update yaf_System set AvatarUpload=1,AvatarRemote=1 where AvatarUpload is null and AvatarRemote is null
GO

alter table yaf_System alter column AvatarUpload bit not null
GO

alter table yaf_System alter column AvatarRemote bit not null
GO

if not exists(select * from syscolumns where id=object_id('yaf_System') and name='AvatarSize')
	alter table yaf_System add AvatarSize int null
GO

if not exists(select * from syscolumns where id=object_id('yaf_System') and name='ShowGroups')
	alter table yaf_System add ShowGroups bit null
GO

update yaf_System set ShowGroups=1 where ShowGroups is null
GO

alter table yaf_System alter column ShowGroups bit not null
GO

if exists (select * from sysobjects where id = object_id(N'yaf_forum_listread') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
	drop procedure yaf_forum_listread
GO

create procedure yaf_forum_listread(@UserID int,@CategoryID int=null) as
begin
	select 
		a.CategoryID, 
		Category		= a.Name, 
		ForumID			= b.ForumID,
		Forum			= b.Name, 
		Description,
		Topics			= (select count(distinct x.TopicID) from yaf_Topic x,yaf_Message y where x.ForumID=b.ForumID and y.TopicID=x.TopicID and y.Approved<>0),
		Posts			= (select count(1) from yaf_Message x,yaf_Topic y where y.TopicID=x.TopicID and y.ForumID = b.ForumID and x.Approved<>0),
		LastPosted		= b.LastPosted,
		LastMessageID	= b.LastMessageID,
		LastUserID		= b.LastUserID,
		LastUser		= IsNull(b.LastUserName,(select Name from yaf_User x where x.UserID=b.LastUserID)),
		LastTopicID		= b.LastTopicID,
		LastTopicName	= (select x.Topic from yaf_Topic x where x.TopicID=b.LastTopicID),
		b.Locked,
		b.Moderated,
		PostAccess		= (select count(1) from yaf_UserGroup x,yaf_ForumAccess y where x.UserID=@UserID and x.GroupID=y.GroupID and y.ForumID=b.ForumID and y.PostAccess<>0),
		ReplyAccess		= (select count(1) from yaf_UserGroup x,yaf_ForumAccess y where x.UserID=@UserID and x.GroupID=y.GroupID and y.ForumID=b.ForumID and y.ReplyAccess<>0),
		ReadAccess		= (select count(1) from yaf_UserGroup x,yaf_ForumAccess y where x.UserID=@UserID and x.GroupID=y.GroupID and y.ForumID=b.ForumID and y.ReadAccess<>0)		
	from 
		yaf_Category a, 
		yaf_Forum b
	where 
		a.CategoryID = b.CategoryID and
		(b.Hidden=0 or exists(select 1 from yaf_UserGroup x,yaf_ForumAccess y where x.UserID=@UserID and x.GroupID=y.GroupID and y.ForumID=b.ForumID and y.ReadAccess<>0)) and
		(@CategoryID is null or a.CategoryID = @CategoryID)
	order by
		a.SortOrder,
		b.SortOrder
end
GO

if exists (select * from sysobjects where id = object_id(N'yaf_group_save') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
	drop procedure yaf_group_save
GO

create procedure yaf_group_save(
	@GroupID		int,
	@Name			varchar(50),
	@IsAdmin		bit,
	@IsGuest		bit,
	@IsStart		bit,
	@IsModerator	bit
) as
begin
	if @IsAdmin = 1 update yaf_Group set IsAdmin = 0
	if @IsGuest = 1 update yaf_Group set IsGuest = 0
	if @IsStart = 1 update yaf_Group set IsStart = 0
	if @GroupID>0 begin
		update yaf_Group set
			Name = @Name,
			IsAdmin = @IsAdmin,
			IsGuest = @IsGuest,
			IsStart = @IsStart,
			IsModerator = @IsModerator
		where GroupID = @GroupID
	end
	else begin
		insert into yaf_Group(Name,IsAdmin,IsGuest,IsStart,IsModerator)
		values(@Name,@IsAdmin,@IsGuest,@IsStart,@IsModerator);
		set @GroupID = @@IDENTITY
		insert into yaf_ForumAccess(GroupID,ForumID,ReadAccess,PostAccess,ReplyAccess,PriorityAccess,PollAccess,VoteAccess,ModeratorAccess,EditAccess,DeleteAccess,UploadAccess)
		select @GroupID,ForumID,0,0,0,0,0,0,0,0,0,0 from yaf_Forum
	end
	select GroupID = @GroupID
end
GO

if exists (select * from sysobjects where id = object_id(N'yaf_pageload') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
	drop procedure yaf_pageload
GO

create procedure yaf_pageload(
	@SessionID	varchar(24),
	@User		varchar(50),
	@IP			varchar(15),
	@Location	varchar(50),
	@Browser	varchar(50),
	@Platform	varchar(50),
	@CategoryID	int = null,
	@ForumID	int = null,
	@TopicID	int = null,
	@MessageID	int = null
) as
begin
	declare @UserID int
	if @User is null or @User='' 
		select @UserID = a.UserID from yaf_User a,yaf_UserGroup b,yaf_Group c where a.UserID=b.UserID and b.GroupID=c.GroupID and c.IsGuest=1
	else
		select @UserID = UserID from yaf_User where Name = @User
	-- Check valid ForumID
	if @ForumID is not null and not exists(select 1 from yaf_Forum where ForumID=@ForumID) begin
		set @ForumID = null
	end
	-- Check valid CategoryID
	if @CategoryID is not null and not exists(select 1 from yaf_Category where CategoryID=@CategoryID) begin
		set @CategoryID = null
	end
	-- Check valid MessageID
	if @MessageID is not null and not exists(select 1 from yaf_Message where MessageID=@MessageID) begin
		set @MessageID = null
	end
	-- Check valid TopicID
	if @TopicID is not null and not exists(select 1 from yaf_Topic where TopicID=@TopicID) begin
		set @TopicID = null
	end

	-- update last visit
	update yaf_User set 
		LastVisit = getdate(),
		IP = @IP
	where UserID = @UserID
	-- find missing ForumID/TopicID
	if @MessageID is not null begin
		select
			@CategoryID = c.CategoryID,
			@ForumID = b.ForumID,
			@TopicID = b.TopicID
		from
			yaf_Message a,
			yaf_Topic b,
			yaf_Forum c
		where
			a.MessageID = @MessageID and
			b.TopicID = a.TopicID and
			c.ForumID = b.ForumID
	end
	else if @TopicID is not null begin
		select 
			@CategoryID = b.CategoryID,
			@ForumID = a.ForumID 
		from 
			yaf_Topic a,
			yaf_Forum b
		where 
			a.TopicID = @TopicID and
			b.ForumID = a.ForumID
	end
	else if @ForumID is not null begin
		select
			@CategoryID = a.CategoryID
		from
			yaf_Forum a
		where
			a.ForumID = @ForumID
	end
	-- update active
	if @UserID is not null begin
		declare @count int
		select @count = count(1) from yaf_Active where SessionID = @SessionID
		if @count>0 begin
			update yaf_Active set
				UserID = @UserID,
				IP = @IP,
				LastActive = getdate(),
				Location = @Location,
				ForumID = @ForumID,
				TopicID = @TopicID,
				Browser = @Browser,
				Platform = @Platform
			where SessionID = @SessionID
		end
		else begin
			insert into yaf_Active(SessionID,UserID,IP,Login,LastActive,Location,ForumID,TopicID,Browser,Platform)
			values(@SessionID,@UserID,@IP,getdate(),getdate(),@Location,@ForumID,@TopicID,@Browser,@Platform)
		end
	end
	-- return information
	select
		a.UserID,
		UserName			= a.Name,
		IsAdmin				= (select count(1) from yaf_UserGroup x,yaf_Group y where x.UserID=a.UserID and x.GroupID=y.GroupID and y.IsAdmin<>0),
		IsGuest				= (select count(1) from yaf_UserGroup x,yaf_Group y where x.UserID=a.UserID and x.GroupID=y.GroupID and y.IsGuest<>0),
		IsForumModerator	= (select count(1) from yaf_UserGroup x,yaf_Group y where x.UserID=a.UserID and x.GroupID=y.GroupID and y.IsModerator<>0),
		IsModerator			= (select count(1) from yaf_ForumAccess x,yaf_UserGroup y where y.UserID=a.UserID and x.GroupID=y.GroupID and x.ModeratorAccess<>0),
		ReadAccess			= (select count(1) from yaf_ForumAccess x,yaf_UserGroup y where y.UserID=a.UserID and x.GroupID=y.GroupID and x.ForumID=@ForumID and x.ReadAccess<>0),
		PostAccess			= (select count(1) from yaf_ForumAccess x,yaf_UserGroup y where y.UserID=a.UserID and x.GroupID=y.GroupID and x.ForumID=@ForumID and x.PostAccess<>0),
		ReplyAccess			= (select count(1) from yaf_ForumAccess x,yaf_UserGroup y where y.UserID=a.UserID and x.GroupID=y.GroupID and x.ForumID=@ForumID and x.ReplyAccess<>0),
		PriorityAccess		= (select count(1) from yaf_ForumAccess x,yaf_UserGroup y where y.UserID=a.UserID and x.GroupID=y.GroupID and x.ForumID=@ForumID and x.PriorityAccess<>0),
		PollAccess			= (select count(1) from yaf_ForumAccess x,yaf_UserGroup y where y.UserID=a.UserID and x.GroupID=y.GroupID and x.ForumID=@ForumID and x.PollAccess<>0),
		VoteAccess			= (select count(1) from yaf_ForumAccess x,yaf_UserGroup y where y.UserID=a.UserID and x.GroupID=y.GroupID and x.ForumID=@ForumID and x.VoteAccess<>0),
		ModeratorAccess		= (select count(1) from yaf_ForumAccess x,yaf_UserGroup y where y.UserID=a.UserID and x.GroupID=y.GroupID and x.ForumID=@ForumID and x.ModeratorAccess<>0),
		EditAccess			= (select count(1) from yaf_ForumAccess x,yaf_UserGroup y where y.UserID=a.UserID and x.GroupID=y.GroupID and x.ForumID=@ForumID and x.EditAccess<>0),
		DeleteAccess		= (select count(1) from yaf_ForumAccess x,yaf_UserGroup y where y.UserID=a.UserID and x.GroupID=y.GroupID and x.ForumID=@ForumID and x.DeleteAccess<>0),
		UploadAccess		= (select count(1) from yaf_ForumAccess x,yaf_UserGroup y where y.UserID=a.UserID and x.GroupID=y.GroupID and x.ForumID=@ForumID and x.UploadAccess<>0),
		CategoryID			= @CategoryID,
		CategoryName		= (select Name from yaf_Category where CategoryID = @CategoryID),
		ForumID				= @ForumID,
		ForumName			= (select Name from yaf_Forum where ForumID = @ForumID),
		TopicID				= @TopicID,
		TopicName			= (select Topic from Yaf_Topic where TopicID = @TopicID),
		TimeZoneUser		= a.TimeZone,
		TimeZoneForum		= s.TimeZone,
		BBName				= s.Name,
		SmtpServer			= s.SmtpServer,
		SmtpUserName		= s.SmtpUserName,
		SmtpUserPass		= s.SmtpUserPass,
		ForumEmail			= s.ForumEmail,
		EmailVerification	= s.EmailVerification,
		BlankLinks			= s.BlankLinks,
		ShowMoved			= s.ShowMoved,
		ShowGroups			= s.ShowGroups,
		MailsPending		= (select count(1) from yaf_Mail),
		Incoming			= (select count(1) from yaf_PMessage where ToUserID=a.UserID and IsRead=0)
	from
		yaf_User a,
		yaf_System s
	where
		a.UserID = @UserID
end
GO

if exists (select * from sysobjects where id = object_id(N'yaf_system_initialize') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
	drop procedure yaf_system_initialize
GO

create procedure yaf_system_initialize(
	@Name		varchar(50),
	@TimeZone	int,
	@ForumEmail	varchar(50),
	@SmtpServer	varchar(50),
	@User		varchar(50),
	@UserEmail	varchar(50),
	@Password	varchar(32)
) as 
begin
	declare @GroupID int
	declare @RankID int
	declare @UserID int

	insert into yaf_System(SystemID,Version,VersionName,Name,TimeZone,SmtpServer,ForumEmail,AvatarWidth,AvatarHeight,AvatarUpload,AvatarRemote,EmailVerification,ShowMoved,BlankLinks,ShowGroups)
	values(1,1,'0.7.0',@Name,@TimeZone,@SmtpServer,@ForumEmail,50,80,0,0,1,1,0,1)

	insert into yaf_Rank(Name,IsStart,IsLadder)
	values('Administration',0,0)
	set @RankID = @@IDENTITY

	insert into yaf_Group(Name,IsAdmin,IsGuest,IsStart)
	values('Administration',1,0,0)
	set @GroupID = @@IDENTITY

	insert into yaf_User(RankID,Name,Password,Joined,LastVisit,NumPosts,TimeZone,Approved,Email)
	values(@RankID,@User,@Password,getdate(),getdate(),0,@TimeZone,1,@UserEmail)
	set @UserID = @@IDENTITY

	insert into yaf_UserGroup(UserID,GroupID) values(@UserID,@GroupID)

	insert into yaf_Rank(Name,IsStart,IsLadder)
	values('Guest',0,0)
	set @RankID = @@IDENTITY

	insert into yaf_Group(Name,IsAdmin,IsGuest,IsStart)
	values('Guest',0,1,0)
	set @GroupID = @@IDENTITY

	insert into yaf_User(RankID,Name,Password,Joined,LastVisit,NumPosts,TimeZone,Approved,Email)
	values(@RankID,'Guest','na',getdate(),getdate(),0,@TimeZone,1,@ForumEmail)
	set @UserID = @@IDENTITY

	insert into yaf_UserGroup(UserID,GroupID) values(@UserID,@GroupID)

	-- users starts as Newbie
	insert into yaf_Rank(Name,IsStart,IsLadder,MinPosts)
	values('Newbie',1,1,0)

	-- advances to Member
	insert into yaf_Rank(Name,IsStart,IsLadder,MinPosts)
	values('Member',0,1,10)

	-- and ends up as Advanced Member
	insert into yaf_Rank(Name,IsStart,IsLadder,MinPosts)
	values('Advanced Member',0,1,30)

	insert into yaf_Group(Name,IsAdmin,IsGuest,IsStart)
	values('Member',0,0,1)
end
GO

if exists (select * from sysobjects where id = object_id(N'yaf_system_save') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
	drop procedure yaf_system_save
GO

create procedure yaf_system_save(
	@Name				varchar(50),
	@TimeZone			int,
	@SmtpServer			varchar(50),
	@SmtpUserName		varchar(50)=null,
	@SmtpUserPass		varchar(50)=null,
	@ForumEmail			varchar(50),
	@EmailVerification	bit,
	@ShowMoved			bit,
	@BlankLinks			bit,
	@ShowGroups			bit,
	@AvatarWidth		int,
	@AvatarHeight		int,
	@AvatarUpload		bit,
	@AvatarRemote		bit,
	@AvatarSize			int=null
) as
begin
	update yaf_System set
		Name = @Name,
		TimeZone = @TimeZone,
		SmtpServer = @SmtpServer,
		SmtpUserName = @SmtpUserName,
		SmtpUserPass = @SmtpUserPass,
		ForumEmail = @ForumEmail,
		EmailVerification = @EmailVerification,
		ShowMoved = @ShowMoved,
		BlankLinks = @BlankLinks,
		ShowGroups = @ShowGroups,
		AvatarWidth = @AvatarWidth,
		AvatarHeight = @AvatarHeight,
		AvatarUpload = @AvatarUpload,
		AvatarRemote = @AvatarRemote,
		AvatarSize = @AvatarSize
end
GO

if exists (select * from sysobjects where id = object_id(N'yaf_user_deleteavatar') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
	drop procedure yaf_user_deleteavatar
GO

create procedure yaf_user_deleteavatar(@UserID int) as begin
	update yaf_User set AvatarImage = null where UserID = @UserID
end
GO

if exists (select * from sysobjects where id = object_id(N'yaf_post_list') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
	drop procedure yaf_post_list
GO

create procedure yaf_post_list(@TopicID int,@UserID int,@UpdateViewCount smallint=1) as
begin
	set nocount on

	if @UpdateViewCount>0
		update yaf_Topic set Views = Views + 1 where TopicID = @TopicID

	select
		d.TopicID,
		a.MessageID,
		a.Posted,
		Subject = d.Topic,
		a.Message,
		a.UserID,
		UserName	= IsNull(a.UserName,b.Name),
		b.Joined,
		Posts		= b.NumPosts,
		d.Views,
		d.ForumID,
		Avatar = b.Avatar,
		b.Location,
		b.HomePage,
		b.Signature,
		RankName = c.Name,
		c.RankImage,
		HasAttachments	= (select count(1) from yaf_Attachment x where x.MessageID=a.MessageID),
		HasAvatarImage = (select count(1) from yaf_User x where x.UserID=b.UserID and AvatarImage is not null),
		e.AvatarUpload,
		e.AvatarRemote
	from
		yaf_Message a, 
		yaf_User b,
		yaf_Rank c,
		yaf_Topic d,
		yaf_System e
	where
		a.Approved <> 0 and
		a.TopicID = @TopicID and
		b.UserID = a.UserID and
		c.RankID = b.RankID and
		d.TopicID = a.TopicID
	order by
		a.Posted asc
end
GO

if exists (select * from sysobjects where id = object_id(N'yaf_post_last10user') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
	drop procedure yaf_post_last10user
GO

create procedure yaf_post_last10user(@UserID int,@PageUserID int) as
begin
	set nocount on

	select top 10
		a.Posted,
		Subject = c.Topic,
		a.Message,
		a.UserID,
		UserName = IsNull(a.UserName,b.Name),
		b.Signature,
		c.TopicID
	from
		yaf_Message a, 
		yaf_User b,
		yaf_Topic c,
		yaf_Forum d
	where
		a.Approved <> 0 and
		a.UserID = @UserID and
		b.UserID = a.UserID and
		c.TopicID = a.TopicID and
		d.ForumID = c.ForumID and
		exists(select 1 from yaf_ForumAccess x,yaf_Group y,yaf_UserGroup z where x.ForumID=d.ForumID and y.GroupID=x.GroupID and z.GroupID=y.GroupID and z.UserID=@PageUserID and x.ReadAccess<>0)
	order by
		a.Posted desc
end
GO

if exists (select * from sysobjects where id = object_id(N'yaf_forum_save') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
	drop procedure yaf_forum_save
GO

create procedure yaf_forum_save(
	@ForumID 		int,
	@CategoryID		int,
	@Name			varchar(50),
	@Description	varchar(255),
	@SortOrder		smallint,
	@Locked			bit,
	@Hidden			bit,
	@IsTest			bit,
	@Moderated		bit,
	@TemplateID		int = null
) as
begin
	if @ForumID>0 begin
		update yaf_Forum set 
			Name=@Name,
			Description=@Description,
			SortOrder=@SortOrder,
			Hidden=@Hidden,
			Locked=@Locked,
			CategoryID=@CategoryID,
			IsTest = @IsTest,
			Moderated = @Moderated
		where ForumID=@ForumID
	end
	else begin
		insert into yaf_Forum(Name,Description,SortOrder,Hidden,Locked,CategoryID,IsTest,Moderated)
		values(@Name,@Description,@SortOrder,@Hidden,@Locked,@CategoryID,@IsTest,@Moderated)
		select @ForumID = @@IDENTITY

		if @TemplateID is not null
			insert into yaf_ForumAccess(GroupID,ForumID,ReadAccess,PostAccess,ReplyAccess,PriorityAccess,PollAccess,VoteAccess,ModeratorAccess,EditAccess,DeleteAccess,UploadAccess) 
			select GroupID,@ForumID,ReadAccess,PostAccess,ReplyAccess,PriorityAccess,PollAccess,VoteAccess,ModeratorAccess,EditAccess,DeleteAccess,UploadAccess
			from yaf_ForumAccess where ForumID=@TemplateID
		else
			insert into yaf_ForumAccess(GroupID,ForumID,ReadAccess,PostAccess,ReplyAccess,PriorityAccess,PollAccess,VoteAccess,ModeratorAccess,EditAccess,DeleteAccess,UploadAccess) 
			select GroupID,@ForumID,0,0,0,0,0,0,0,0,0,0 from yaf_Group
	end
	select ForumID = @ForumID
end
GO

if exists (select * from sysobjects where id = object_id(N'yaf_user_activity_rank') and OBJECTPROPERTY(id, N'IsProcedure') = 1)
	drop procedure yaf_user_activity_rank
GO

create procedure yaf_user_activity_rank(@StartDate as datetime) AS
begin
	select top 3  ID, Name, NumOfPosts from yaf_User u inner join
	(
		select m.UserID as ID, Count(m.UserID) as NumOfPosts from yaf_Message m
		where m.Posted >= @StartDate
		group by m.UserID
	) as counter
	on u.UserID = counter.ID
	order by NumOfPosts desc
end
GO
