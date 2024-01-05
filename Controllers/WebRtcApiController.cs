using Microsoft.AspNetCore.Mvc;
using System.Net.WebSockets;
using System.Net;
using System.Text.RegularExpressions;
using System.Text;
using System;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace WebRtc桌面共享.Controllers
{
    [Route("api/WebRtcApi/[action]")]
    public class WebRtcApiController : ControllerBase
    {
        private readonly ILogger<WebRtcApiController> _logger;
        private readonly IHttpContextAccessor _accessor;
        public WebRtcApiController(ILogger<WebRtcApiController> logger, IHttpContextAccessor accessor)
        {
            _logger = logger;
            _accessor = accessor;
        }
        /// <summary>
        /// 离线消息
        /// </summary>
        public class MessageInfo
        {
            public MessageInfo(DateTime _MsgTime, ArraySegment<byte> _MsgContent)
            {
                MsgTime = _MsgTime;
                MsgContent = _MsgContent;
            }
            public DateTime MsgTime { get; set; }
            public ArraySegment<byte> MsgContent { get; set; }
        }

        public class UserInfo
        {
            public UserInfo(string _IP, WebSocket _websocket, DateTime _loginTime, string _Remote_Id)
            {
                IP = _IP;
                websocket = _websocket;
                loginTime = _loginTime;
                Remote_Id = _Remote_Id;
            }
            public string IP { get; set; }
            public WebSocket websocket { get; set; }
            public DateTime loginTime { get; set; }
            public string Remote_Id { get; set; }
        }



        private static Dictionary<string, UserInfo> CONNECT_POOL = new Dictionary<string, UserInfo>();//用户连接池
        private static Dictionary<string, List<MessageInfo>> MESSAGE_POOL = new Dictionary<string, List<MessageInfo>>();//离线消息池
        Boolean recive_flag = false;
        string recivemsg = "";

        [Route("/ws")]
        public async Task Get()
        {
            if (HttpContext.WebSockets.IsWebSocketRequest)
            {
                await ProcessChat(HttpContext);
            }
        else
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        }

}

        private async Task ProcessChat(HttpContext context)
        {
            var socket = await context.WebSockets.AcceptWebSocketAsync();
            string USERID = context.Request.Query["USERID"].ToString();
            string IP = context.Request.Query["IP"].ToString();

            UserInfo userInfo = new UserInfo(IP, socket, DateTime.Now, "");


            try
            {
                #region 用户添加连接池
                //第一次open时，添加到连接池中
                if (!CONNECT_POOL.ContainsKey(USERID))
                    CONNECT_POOL.Add(USERID, userInfo);//不存在，添加
                else
                    if (socket != CONNECT_POOL[USERID].websocket)//当前对象不一致，更新
                    CONNECT_POOL[USERID].websocket = socket;
                #endregion

                //广播推送所有在线用户列表数据
                foreach (var item in CONNECT_POOL)
                {
                    SendMsg(item.Value.websocket, msgHandler_B("userList", USERID));
                    //await item.Value.websocket.SendAsync(TEXT_TO_BYTE(msgHandler_B("userList", USERID)), WebSocketMessageType.Text, true, CancellationToken.None);

                }

                //#region 离线消息处理
                //if (MESSAGE_POOL.ContainsKey(USERID))
                //{
                //    List<MessageInfo> msgs = MESSAGE_POOL[USERID];
                //    foreach (MessageInfo item in msgs)
                //    {
                //        await socket.SendAsync(item.MsgContent, WebSocketMessageType.Text, true, CancellationToken.None);
                //    }
                //    MESSAGE_POOL.Remove(USERID);//移除离线消息
                //}
                //#endregion

                string descUser = string.Empty;//目的用户
                while (true)
                {
                    if (socket.State == WebSocketState.Open)
                    {
                        ArraySegment<byte> buffer = new ArraySegment<byte>(new byte[4096]);

                        //await socket.SendAsync(TEXT_TO_BYTE(msgHandler_B("userList",USERID)), WebSocketMessageType.Text, true, CancellationToken.None);
                        WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, CancellationToken.None);

                        #region 消息处理（字符截取、消息转发）
                        try
                        {
                            #region 关闭Socket处理，删除连接池
                            if (socket.State != WebSocketState.Open)//连接关闭
                            {
                                if (CONNECT_POOL.ContainsKey(USERID)) CONNECT_POOL.Remove(USERID);//删除连接池
                                break;
                            }
                            #endregion



                            string userMsg = Encoding.UTF8.GetString(buffer.Array, 0, result.Count);//发送过来的消息
                            if (is_recive(userMsg) || recive_flag)
                            {
                                recivemsg += userMsg;
                                recive_flag = is_recive(userMsg);

                            }

                            if (!string.IsNullOrEmpty(recivemsg) && !recive_flag)
                            {
                                msgHandler_S(recivemsg, USERID);
                                recive_flag = false;
                                recivemsg = "";
                            }
                            else if (string.IsNullOrEmpty(recivemsg))
                            {
                                msgHandler_S(userMsg, USERID);
                            }




                            //Task.Run(() =>
                            //{
                            //    if (!MESSAGE_POOL.ContainsKey(descUser))//将用户添加至离线消息池中
                            //        MESSAGE_POOL.Add(descUser, new List<MessageInfo>());
                            //    MESSAGE_POOL[descUser].Add(new MessageInfo(DateTime.Now, buffer));//添加离线消息
                            //});

                        }
                        catch (Exception exs)
                        {
                            //消息转发异常处理，本次消息忽略 继续监听接下来的消息
                        }
                        #endregion
                    }
                    else
                    {
                        break;
                    }
                }//while end
            }
            catch (Exception ex)
            {
                //整体异常处理
                if (CONNECT_POOL.ContainsKey(USERID)) CONNECT_POOL.Remove(USERID);
            }
        }

        [HttpGet]
        /// <summary>
        /// 客户端调用服务端方法
        /// </summary>
        /// <param name = "MSG" > 前端返回信息 </ param >
        /// < param name="USERID">客户标识ID</param>
        public void msgHandler_S(string MSG, string USERID)
        {

            string remoteUserId = match_key("remoteUserId", MSG);
            switch (match_key("type", MSG))
            {
                case "setUpCall":
                    if (!CONNECT_POOL.ContainsKey(remoteUserId))//判断客户端是否在线
                    {
                        // await CONNECT_POOL[USERID].websocket.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
                        SendMsg(CONNECT_POOL[USERID].websocket, ErrorMsg("客户端" + remoteUserId + "已离线"));
                        break;
                    }
                    CONNECT_POOL[USERID].Remote_Id = remoteUserId;
                    JObject result_msg_setUpCall = new JObject();
                    result_msg_setUpCall.Add("type", "recvCall");

                    JObject data_msg_setUpCall = new JObject();
                    data_msg_setUpCall.Add(new JProperty("userId", USERID));
                    data_msg_setUpCall.Add(new JProperty("ip", CONNECT_POOL[USERID].IP));

                    result_msg_setUpCall.Add("data", data_msg_setUpCall);

                    result_msg_setUpCall.ToString();

                    SendMsg(CONNECT_POOL[remoteUserId].websocket, result_msg_setUpCall.ToString());
                    break;
                case "rejectCall":
                    CONNECT_POOL[USERID].Remote_Id = remoteUserId;
                    JObject result_msg_rejectCall = new JObject();
                    result_msg_rejectCall.Add("type", "callRejected");
                    SendMsg(CONNECT_POOL[CONNECT_POOL[USERID].Remote_Id].websocket, result_msg_rejectCall.ToString());
                    break;
                case "answerCall":
                    CONNECT_POOL[USERID].Remote_Id = remoteUserId;
                    JObject result_msg_answerCall = new JObject();
                    result_msg_answerCall.Add("type", "callAnswered");
                    SendMsg(CONNECT_POOL[CONNECT_POOL[USERID].Remote_Id].websocket, result_msg_answerCall.ToString());
                    break;
                case "candidate":
                    CONNECT_POOL[USERID].Remote_Id = remoteUserId;
                    JObject result_msg_candidate = new JObject();
                    result_msg_candidate.Add("type", "remoteCandidate");
                    JObject data_msg_candidate = new JObject();
                    data_msg_candidate.Add(new JProperty("candidate", match_key("candidate", MSG)));
                    data_msg_candidate.Add(new JProperty("sdpMid", match_key("sdpMid", MSG)));
                    data_msg_candidate.Add(new JProperty("sdpMLineIndex", match_key("sdpMLineIndex", MSG)));

                    result_msg_candidate.Add("data", data_msg_candidate);
                    SendMsg(CONNECT_POOL[remoteUserId].websocket, result_msg_candidate.ToString());
                    break;
                case "offer":
                    //   JObject result_msg_offer = new JObject();
                    // result_msg_offer.Add("type", "offer");
                    // JObject data_msg_offer = new JObject();
                    // data_msg_offer.Add(new JProperty("desc", match_key("desc", MSG)));

                    // result_msg_offer.Add("data", data_msg_offer);
                    SendMsg(CONNECT_POOL[CONNECT_POOL[USERID].Remote_Id].websocket, MSG);
                    break;
                case "answer":
                    //JObject result_msg_answer = new JObject();
                    //result_msg_answer.Add("type", "answer");
                    //JObject data_msg_answer = new JObject();
                    //data_msg_answer.Add(new JProperty("desc", match_key("desc", MSG)));

                    //result_msg_answer.Add("data", data_msg_answer);
                    SendMsg(CONNECT_POOL[CONNECT_POOL[USERID].Remote_Id].websocket, MSG);
                    break;
                default:
                    SendMsg(CONNECT_POOL[USERID].websocket, ErrorMsg("无效的访问！"));
                    break;

            }


        }

        [HttpGet]
        /// <summary>
        /// 服务端调用客户端方法
        /// </summary>
        /// <param name="option"></param>
        /// <param name="USERID"></param>
        public string msgHandler_B_Old(string option, string USERID)
        {
            StringBuilder Useinfo_content = new StringBuilder();
            Useinfo_content.Append("{");
            Useinfo_content.Append("\"type\":\"" + option + "\"");




            switch (option)
            {
                case "userList":
                    Useinfo_content.Append(",\"data\":");
                    Useinfo_content.Append("[");
                    foreach (var item in CONNECT_POOL)
                    {
                        Useinfo_content.Append(ToJson(new { userId = item.Key, ip = item.Value.IP, loginTime = item.Value.loginTime }) + ",");
                    }
                    Useinfo_content = Useinfo_content.Remove(Useinfo_content.Length - 1, 1);
                    Useinfo_content.Append("]");
                    break;
                case "remoteCandidate":
                    break;
                case "recvCall":
                    break;
                case "callRejected":
                    break;
                case "callAnswered":
                    break;
                case "offer":
                    break;
                case "answer":
                    break;
                default:
                    return "";
            }
            Useinfo_content.Append("}");
            return Useinfo_content.ToString();
        }

        [HttpGet]
        /// <summary>
        /// 服务端调用客户端方法
        /// </summary>
        /// <param name="option"></param>
        /// <param name="USERID"></param>
        public string msgHandler_B(string option, string USERID)
        {
            JObject result_msg = new JObject();
            result_msg.Add("type", option);



            switch (option)
            {
                case "userList":
                    JArray userInfoList = new JArray();
                    foreach (var item in CONNECT_POOL)
                    {
                        JObject userInfo = new JObject();
                        userInfo.Add("userId", item.Key);
                        userInfo.Add("ip", item.Value.IP);
                        userInfo.Add("loginTime", item.Value.loginTime);
                        userInfoList.Add(userInfo);
                    }
                    result_msg.Add(new JProperty("data", userInfoList));
                    break;
                case "error":
                    break;
                default:
                    return "";
            }
            return result_msg.ToString();
        }



        /// <summary>
        /// 将文本转化为字节流
        /// </summary>
        /// <param name="text">待转换文本</param>
        /// <returns></returns>
        private ArraySegment<byte> TEXT_TO_BYTE(string text)
        {

            return new ArraySegment<byte>(Encoding.UTF8.GetBytes(text));
        }


        private string ToJson(Object obj)
        {
             return JsonConvert.SerializeObject(obj);
        }

        /// <summary>
        /// 根据关键字匹配json中相应字段的值
        /// </summary>
        /// <param name="key">字段名</param>
        /// <param name="srcstring">json字符串</param>
        /// <returns></returns>
        private string match_key(string key, string srcstring)
        {
            Dictionary<string, string> match_regex = new Dictionary<string, string>();
            string result = "";

            //  match_regex.Add("regexStr1", "{\"" + key + "\":{(?<key>.*?)}");
            match_regex.Add("regexStr2", "\"" + key + "\":\"(?<key>.*?)\"");
            match_regex.Add("regexStr3", "\"" + key + "\":{(?<key>.*?)}}");
            match_regex.Add("regexStr4", "\"" + key + "\":(?<key>.*?),");


            foreach (var item in match_regex)
            {
                if (!string.IsNullOrEmpty(match_fun(item.Value, srcstring)))
                {
                    result = match_fun(item.Value, srcstring);
                    break;
                }

            }



            return result;

        }


        private string ErrorMsg(string msg)
        {
            JObject ErrorMsgContent = new JObject();
            ErrorMsgContent.Add("type", "error");
            JObject ErrorMsgData = new JObject();
            ErrorMsgData.Add(new JProperty("msg", msg));
            ErrorMsgContent.Add("data", ErrorMsgData);
            return ErrorMsgContent.ToString();
        }

        [HttpGet]
        /// <summary>
        /// 向指定客户端推送信息
        /// </summary>
        /// <param name="wb"></param>
        /// <param name="msg"></param>
        public async Task SendMsg(WebSocket wb, string msg)
        {
            if (wb != null && wb.State == WebSocketState.Open)
            {
                await wb.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(msg)), WebSocketMessageType.Text, true, CancellationToken.None);
            }

        }

        private string match_fun(string regexStr, string srcstring)
        {
            Regex r = new Regex(regexStr, RegexOptions.None);
            Match mc = r.Match(srcstring);
            return mc.Groups["key"].Value;
        }


        private Boolean is_recive(string msg)
        {
            if (msg.Substring(msg.Length - 1, 1) != "}")
            {
                return true;
            }
            else
            {
                return false;
            }

        }
    }
}
