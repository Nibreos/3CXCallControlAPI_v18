﻿using System;
using System.Threading;
using TCX.Configuration;
using CallFlow;
using System.Threading.Tasks;
using System.Linq;

/// <summary>
/// namespace is substituted by scripting for own needs.
/// </summary>
namespace dummy
{
    /// <summary>
    /// this is samle shows how to implement CallFlow route point which will make a connection between two entities specified in S_QCALLBACK request.
    /// prerequisites:
    /// S_QCALLBACK request (Statistics object) is created with following data:
    /// Statistics.GetName() - unique identified of the request. This part is mandatory and uniqueu, so it can be a string which contains data provided by external application (d.e. unique id of the call which is provided by CRM application)
    /// When call is coming from QCB service, MyCall["public_qticket"] specifies the name of the S_QCALLBACK statistics.
    /// S_QCALLBACK allows to extend storage of the request with additional "pv_*" but this feature must be encapsulated. It mean that the final implemention of call generators must not use (or expose) low level API which allow to add such fields.
    /// set of the built in fields are:
    /// S_QCALLBACK.GetName() - unique identifier generated by the requestor. We can use agreement where all S_QCALLBACK names which are generated for dialer scripts are starting with the Dialer DN.Number and the rest is the external system (and/or call generator) specific id.
    /// fields:
    /// "queue" - It is the destination of the call for QCB. It MUST be the dialer DN.Number.
    /// "destination" - the destination which should be connected with the "queue". it is second step of the callback which is executed after "queue" will accept the call.
    /// "display_name" - the displayname of the destinaltion. Can also be used to deliver some additional information.
    /// "result" - this is either "success" or "failure". this is cumulative result of the call back. it means that the desired parties ("queue" and "destination" where not connected)
    /// "reason" - may be specified as explanation of the "result"
    /// "request" - "pending", "active", "cancelled" - it is the state of the initial call request on QCB side.
    /// "pv_owner" - mandatory. Identifier of subsystem created the request. this field is empty for QueueManager. initiator must specify it and should remove own requests at the end.
    /// initiator must track own requests and remove them when request is done.
    /// Top level logic of the dialer:
    /// accept the call. Route it to the source of call (use transfer, divert or what ever is required)
    /// when source will be connected, QCB will deliver it to the destination
    /// This call will appear in the call history
    public class Dialer : ScriptBase<Dialer>
    {
        string MakeSourceAddress(string displayName)
        {
            return $"\"{displayName}\"<sip:_{MyCall.Caller.CallerID}@{MyCall.PS.GetParameterValue("SIPDOMAIN")}>;nofwd=1";
        }

        /// <summary>
        /// script entry point.
        /// Here we are subscribe for necessary event and initiate process which will repeatedly deliver call to the returnTo with "CallReminder:" prefix.
        /// </summary>
        public async override void Start()
        {
            //when call will leave RoutePoint we just dispose timer and put message into 3CXCallFlow service log.
            try
            {
                MyCall.Info($"{string.Join("\n", MyCall.AttachedData.Select(x => $"{x.Key}={x.Value}"))}");
                await MyCall.AssureMedia()
                .ContinueWith(_ =>
                {
                    var stat = ((PhoneSystem)MyCall.PS).CreateStatistics("S_QCALLBACK", MyCall["public_qticket"]);
                    MyCall.Info($"\n{stat}:\n{string.Join("\n    ", stat.Content.Select(x => $"{x.Key}={x.Value}"))}");
                    var nameaddr = MakeSourceAddress(stat["display_name"]);
                    var timeout = 30;
                    MyCall.Info($"MyCall.RouteTo({stat["pv_source"]}, {nameaddr}, {timeout}");
                    MyCall.OnTerminated += () =>
                    {
                        MyCall.Info($"Successful route to '{stat["pv_source"]}' for callback: {MyCall}");
                    };
                    return MyCall.RouteTo(stat["pv_source"], MakeSourceAddress(stat["display_name"]), timeout);
                }).Unwrap()
                .ContinueWith(_ =>
                {
                    if (_.Result)
                    {
                        MyCall.Info("Source did not answer the call");
                        MyCall.Return(false);
                    }
                    else
                    {
                        MyCall.Info("Cannot reach the source");
                        MyCall.Return(false);
                    }
                }
                , TaskContinuationOptions.OnlyOnRanToCompletion);
            }
            catch(Exception ex)
            {
                MyCall.Return(false);
            }
        }
    }
}
