#region Copyright
// /************************************************************************
//    Copyright (c) 2016 Jamie Rees
//    File: ApprovalModule.cs
//    Created By: Jamie Rees
//   
//    Permission is hereby granted, free of charge, to any person obtaining
//    a copy of this software and associated documentation files (the
//    "Software"), to deal in the Software without restriction, including
//    without limitation the rights to use, copy, modify, merge, publish,
//    distribute, sublicense, and/or sell copies of the Software, and to
//    permit persons to whom the Software is furnished to do so, subject to
//    the following conditions:
//   
//    The above copyright notice and this permission notice shall be
//    included in all copies or substantial portions of the Software.
//   
//    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
//    EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
//    MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
//    NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
//    LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
//    OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
//    WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//  ************************************************************************/
#endregion
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Nancy;
using Nancy.Security;

using NLog;
using PlexRequests.Api;
using PlexRequests.Api.Interfaces;
using PlexRequests.Core;
using PlexRequests.Core.SettingModels;
using PlexRequests.Helpers;
using PlexRequests.Store;
using PlexRequests.UI.Helpers;
using PlexRequests.UI.Models;

namespace PlexRequests.UI.Modules
{
    public class ApprovalModule : BaseModule
    {

        public ApprovalModule(IRequestService service, ISettingsService<CouchPotatoSettings> cpService, ICouchPotatoApi cpApi, ISonarrApi sonarrApi,
            ISettingsService<SonarrSettings> sonarrSettings, ISickRageApi srApi, ISettingsService<SickRageSettings> srSettings) : base("approval")
        {
            this.RequiresAuthentication();

            Service = service;
            CpService = cpService;
            CpApi = cpApi;
            SonarrApi = sonarrApi;
            SonarrSettings = sonarrSettings;
            SickRageApi = srApi;
            SickRageSettings = srSettings;

            Post["/approve"] = parameters => Approve((int)Request.Form.requestid);
            Post["/approveall"] = x => ApproveAll();
        }

        private IRequestService Service { get; }

        private static Logger Log = LogManager.GetCurrentClassLogger();
        private ISettingsService<SonarrSettings> SonarrSettings { get; }
        private ISettingsService<SickRageSettings> SickRageSettings { get; }
        private ISettingsService<CouchPotatoSettings> CpService { get; }
        private ISonarrApi SonarrApi { get; }
        private ISickRageApi SickRageApi { get; }
        private ICouchPotatoApi CpApi { get; }

        /// <summary>
        /// Approves the specified request identifier.
        /// </summary>
        /// <param name="requestId">The request identifier.</param>
        /// <returns></returns>
        private Response Approve(int requestId)
        {
            Log.Info("approving request {0}", requestId);
            if (!Context.CurrentUser.IsAuthenticated())
            {
                return Response.AsJson(new JsonResponseModel { Result = false, Message = "You are not an Admin, so you cannot approve any requests." });
            }
            // Get the request from the DB
            var request = Service.Get(requestId);

            if (request == null)
            {
                Log.Warn("Tried approving a request, but the request did not exist in the database, requestId = {0}", requestId);
                return Response.AsJson(new JsonResponseModel { Result = false, Message = "There are no requests to approve. Please refresh." });
            }

            switch (request.Type)
            {
                case RequestType.Movie:
                    return RequestMovieAndUpdateStatus(request);
                case RequestType.TvShow:
                    return RequestTvAndUpdateStatus(request);
                default:
                    throw new ArgumentOutOfRangeException(nameof(request));
            }
        }

        private Response RequestTvAndUpdateStatus(RequestedModel request)
        {
            var sender = new TvSender(SonarrApi, SickRageApi);

            var sonarrSettings = SonarrSettings.GetSettings();
            if (sonarrSettings.Enabled)
            {
                Log.Trace("Sending to Sonarr");
                var result = sender.SendToSonarr(sonarrSettings, request);
                Log.Trace("Sonarr Result: ");
                Log.Trace(result.DumpJson());
                if (!string.IsNullOrEmpty(result.title))
                {
                    Log.Info("Sent successfully, Approving request now.");
                    request.Approved = true;
                    var requestResult = Service.UpdateRequest(request);
                    Log.Trace("Approval result: {0}",requestResult);
                    if (requestResult)
                    {
                        return Response.AsJson(new JsonResponseModel { Result = true });
                    }
                    return Response.AsJson(new JsonResponseModel { Result = false, Message = "Updated Sonarr but could not approve it in PlexRequests :("});
                }
                return Response.AsJson(new JsonResponseModel
                {
                    Result = false,
                    Message = "Could not add the series to Sonarr"
                });
            }

            var srSettings = SickRageSettings.GetSettings();
            if (srSettings.Enabled)
            {
                Log.Trace("Sending to SickRage");
                var result = sender.SendToSickRage(srSettings, request);
                Log.Trace("SickRage Result: ");
                Log.Trace(result.DumpJson());
                if (result?.result == "success")
                {
                    Log.Info("Sent successfully, Approving request now.");
                    request.Approved = true;
                    var requestResult = Service.UpdateRequest(request);
                    Log.Trace("Approval result: {0}", requestResult);
                    if (requestResult)
                    {
                        return Response.AsJson(new JsonResponseModel { Result = true });
                    }
                    return Response.AsJson(new JsonResponseModel { Result = false, Message = "Updated SickRage but could not approve it in PlexRequests :(" });
                }
                return Response.AsJson(new JsonResponseModel
                {
                    Result = false,
                    Message = result?.message != null ? "<b>Message From SickRage: </b>" + result.message : "Could not add the series to SickRage"
                });
            }
            return Response.AsJson(new JsonResponseModel
            {
                Result = false,
                Message = "SickRage or Sonarr are not set up!"
            });
        }

        private Response RequestMovieAndUpdateStatus(RequestedModel request)
        {
            if (!Context.CurrentUser.IsAuthenticated())
            {
                return Response.AsJson(new JsonResponseModel { Result = false, Message = "You are not an Admin, so you cannot approve any requests." });
            }

            var cpSettings = CpService.GetSettings();
            var cp = new CouchPotatoApi();
            Log.Info("Adding movie to CP : {0}", request.Title);
            var result = cp.AddMovie(request.ImdbId, cpSettings.ApiKey, request.Title, cpSettings.FullUri, cpSettings.ProfileId);
            Log.Trace("Adding movie to CP result {0}", result);
            if (result)
            {
                // Approve it
                request.Approved = true;

                // Update the record
                var inserted = Service.UpdateRequest(request);

                return Response.AsJson(inserted
                    ? new JsonResponseModel {Result = true}
                    : new JsonResponseModel
                    {
                        Result = false,
                        Message = "We could not approve this request. Please try again or check the logs."
                    });
            }
            return
                Response.AsJson(
                    new
                    {
                        Result = false,
                        Message =
                            "Something went wrong adding the movie to CouchPotato! Please check your settings."
                    });
        }

        /// <summary>
        /// Approves all.
        /// </summary>
        /// <returns></returns>
        private Response ApproveAll()
        {
            var requests = Service.GetAll().Where(x => x.Approved == false);
            var requestedModels = requests as RequestedModel[] ?? requests.ToArray();
            if (!requestedModels.Any())
            {
                return Response.AsJson(new JsonResponseModel { Result = false, Message = "There are no requests to approve. Please refresh." });
            }

            var cpSettings = CpService.GetSettings();


            var updatedRequests = new List<RequestedModel>();
            foreach (var r in requestedModels)
            {
                if (r.Type == RequestType.Movie)
                {
                    var result = SendMovie(cpSettings, r, CpApi);
                    if (result)
                    {
                        r.Approved = true;
                        updatedRequests.Add(r);
                    }
                    else
                    {
                        Log.Error("Could not approve and send the movie {0} to couch potato!", r.Title);
                    }
                }
                if (r.Type == RequestType.TvShow)
                {
                    var sender = new TvSender(SonarrApi,SickRageApi);
                    var sr = SickRageSettings.GetSettings();
                    var sonarr = SonarrSettings.GetSettings();
                    if (sr.Enabled)
                    {
                        var result = sender.SendToSickRage(sr, r);
                        if (result?.result == "success")
                        {
                            r.Approved = true;
                            updatedRequests.Add(r);
                        }
                        else
                        {
                            Log.Error("Could not approve and send the TV {0} to SickRage!", r.Title);
                            Log.Error("SickRage Message: {0}", result?.message);
                        }
                    }

                    if (sonarr.Enabled)
                    {
                        var result = sender.SendToSonarr(sonarr, r);
                        if (result != null)
                        {
                            r.Approved = true;
                            updatedRequests.Add(r);
                        }
                        else
                        {
                            Log.Error("Could not approve and send the TV {0} to Sonarr!", r.Title);
                        }
                    }
                }
            }
            try
            {

                var result = Service.BatchUpdate(updatedRequests); return Response.AsJson(result
                 ? new JsonResponseModel { Result = true }
                 : new JsonResponseModel { Result = false, Message = "We could not approve all of the requests. Please try again or check the logs." });

            }
            catch (Exception e)
            {
                Log.Fatal(e);
                return Response.AsJson(new JsonResponseModel { Result = false, Message = "Something bad happened, please check the logs!" });
            }

        }

        private bool SendMovie(CouchPotatoSettings settings, RequestedModel r, ICouchPotatoApi cp)
        { 
            Log.Info("Adding movie to CP : {0}", r.Title);
            var result = cp.AddMovie(r.ImdbId, settings.ApiKey, r.Title, settings.FullUri, settings.ProfileId);
            Log.Trace("Adding movie to CP result {0}", result);
            return result;
        }
    }
}