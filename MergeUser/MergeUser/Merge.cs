﻿using Countersoft.Gemini;
using Countersoft.Gemini.Commons.Dto;
using Countersoft.Gemini.Commons.Entity;
using Countersoft.Gemini.Controllers.Api;
using Countersoft.Gemini.Extensibility.Apps;
using Countersoft.Gemini.Infrastructure.Managers;
using Countersoft.Gemini.Infrastructure.TimerJobs;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MergeUser
{
    [AppType(AppTypeEnum.Timer),
        AppGuid("6C7AF4E9-B848-41C1-BA8D-3BABD6EBD864"),
        AppName("Merge User"),
        AppDescription("Merge disabled users after a certain time and delete them. The duration from the 'lastupdated' date and also the default user is configurable. These can be configured in the App-Config file.")]

    public class Merge : TimerJob
    {
        /// <summary>
        /// This method filters an email address with a regex pattern to get domain
        /// </summary>
        /// <param name="email"></param>
        /// <returns></returns>
        public string FindDomain(string email)
        {
            string domain = null;
            try
            {
                string pattern = "(?<=@)(.*)";
                Regex regex = new Regex(pattern);
                Match match = regex.Match(email);
                domain = match.Value;
            }
            catch (Exception exception)
            {
                GeminiApp.LogException(exception, false, exception.Message);
            }
            return domain;
        }

        /// <summary>
        /// Get value from different AppConfig settings
        /// </summary>
        /// <param name="settings"></param>
        /// <returns></returns>
        public KeyValueConfigurationElement GetAppConfigSettings(string settings)
        {
            ExeConfigurationFileMap configFile = new ExeConfigurationFileMap();
            string assemblyFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string appConfigFileName = Path.Combine(assemblyFolder, "App.config");
            configFile.ExeConfigFilename = appConfigFileName;
            Configuration config = ConfigurationManager.OpenMappedExeConfiguration(configFile, ConfigurationUserLevel.None);
            AppSettingsSection appSettings =
                   (AppSettingsSection)config.GetSection("appSettings");
            return appSettings.Settings[settings];
        }

        /// <summary>
        /// Get active users from the same domain/organisation as the disabled user
        /// </summary>
        /// <param name="issueManager"></param>
        /// <param name="user"></param>
        /// <returns></returns>
        public List<string> GetActiveUsersFromOrganisation(IssueManager issueManager, User user)
        {
            UserManager userManager = new UserManager(GeminiApp.Cache(), GeminiApp.UserContext(), issueManager.GeminiContext);
            List<UserDto> activeUsers = userManager.GetActiveUsers();
            string DisabledUserDomain = FindDomain(user.Email);
            List<string> activeUserList = new List<string>();

            foreach (var activeUser in activeUsers)
            {
                string ActiveUserDomain = FindDomain(activeUser.Entity.Email);

                if (DisabledUserDomain == ActiveUserDomain)
                {
                    activeUserList.Add(activeUser.Entity.Username);
                }
            }
            activeUserList.Sort();
            return activeUserList;
        }

        /// <summary>
        /// Merge and delete user with creating an auditlog for each action.
        /// </summary>
        /// <param name="activeUser"></param>
        /// <param name="user"></param>
        /// <param name="disabledUserId"></param>
        /// <param name="userManager"></param>
        public void MergeAndDelete(User activeUser, User user, int disabledUserId, UserManager userManager)
        {
            int mergeToUserId = activeUser.Id;
            string mergeToUserOgranisation = activeUser.Email;
            userManager.Merge(mergeToUserId, disabledUserId, false, false, false, false, false, false);
            LogDebugMessage("Merged User: " + user.Email +"/ Merged to user from organisation: " + mergeToUserOgranisation);
            userManager.Delete(disabledUserId);
            LogDebugMessage("Deleted User: " + user.Email);
        }

        /// <summary>
        /// Checks the disabled users and executes the MergeAndDelete-Method
        /// </summary>
        /// <param name="issueManager"></param>
        public void CheckDisabledUsers(IssueManager issueManager)
        {
            UserManager userManager = new UserManager(GeminiApp.Cache(), GeminiApp.UserContext(), issueManager.GeminiContext);
            var allUsers = userManager.GetAll();
            DateTime currentDate = DateTime.Now;
            var time = GetAppConfigSettings("disabledForDays").Value;
            int days = Convert.ToInt32(time);

            foreach (var user in allUsers)
            {
                if (user.Entity.Active == false)
                {
                    var activeUserIndex = GetActiveUsersFromOrganisation(issueManager, user.Entity);
                    DateTime lastupdateIncludeMonths = user.Entity.Revised.AddDays(days);
                    int disabledUserId = user.Entity.Id;
                    if (activeUserIndex.Count != 0 && lastupdateIncludeMonths < DateTime.Now)
                    {
                        string firstActiveUserOrganisation = activeUserIndex[0];

                        foreach (var activeUser in allUsers)
                        {
                            if (activeUser.Entity.Username == firstActiveUserOrganisation)
                            {
                                try
                                {
                                    MergeAndDelete(activeUser.Entity, user.Entity, disabledUserId, userManager);
                                    break;
                                }
                                catch (Exception e)
                                {
                                    string message = string.Format("Folgender User konnte nicht gemerged oder gelöscht werden: {0} ", user.Entity.Fullname);
                                    GeminiApp.LogException(e, false, message);
                                }
                            }
                        }
                    }
                    else if (lastupdateIncludeMonths < DateTime.Now)
                    {
                        foreach (var activeUser in allUsers)
                        {
                            if (activeUser.Entity.Username == GetAppConfigSettings("defaultUser").Value)
                            {
                                try
                                {
                                    MergeAndDelete(activeUser.Entity, user.Entity, disabledUserId, userManager);
                                    break;
                                }
                                catch (Exception e)
                                {
                                    string message = string.Format("Folgender User konnte nicht gemerged oder gelöscht werden: {0} ", user.Entity.Fullname);
                                    GeminiApp.LogException(e, false, message);
                                }
                            }
                        }
                    }
                }
            }
        }

        public override TimerJobSchedule GetInterval(Countersoft.Gemini.Contracts.Business.IGlobalConfigurationWidgetStore dataStore)
        {
            var data = dataStore.Get<TimerJobSchedule>(AppGuid);

            if (data == null || data.Value == null)
            {
                return new TimerJobSchedule(5);
            }
            return data.Value;
        }

        public override bool Run(IssueManager issueManager)
        {
            CheckDisabledUsers(issueManager);
            return true;
        }

        public override void Shutdown()
        {
            throw new NotImplementedException();
        }
    }
}