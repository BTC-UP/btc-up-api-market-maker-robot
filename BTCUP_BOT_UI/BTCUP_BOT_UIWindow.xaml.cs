using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Data.SqlClient;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Web;
using System.Timers;
using BtcE;
using Newtonsoft.Json.Linq;
using System.Web.Script.Serialization;
using System.Globalization;
using System.Windows.Threading;
using System.Threading.Tasks;


namespace BTCUP_BOT_UI
{

    public partial class BTCUP_BOT_UIWindow : Window
    {
        public BTCUP_BOT_UIWindow()
        {
            InitializeComponent();
        }

        DispatcherTimer _timer, _timerCheckOrders;

        #region var
        int timerMs1, timerMs2;
        string key, secret;
        decimal plusRateSell, minusRateSell, minusRateBuy, plusRateBuy;
        double orderRateSpreadBuy, orderRateSpreadSell, orderBuyFinances, orderSellFinances;
        double maxBlockedFinances;
        bool blockBtc = false, blockRub = false;
        #endregion

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button).Content.ToString() == "Start")
            {
                fillGlobalVar();
                
                checkOrdersApi();

                _timer = new DispatcherTimer();
                _timer.Tick += new EventHandler(_timer_Elapsed);
                _timer.Interval = TimeSpan.FromMilliseconds(timerMs1);

                _timer.Start();

                _timerCheckOrders = new DispatcherTimer();
                _timerCheckOrders.Tick += new EventHandler(_timer_Elapsed_checkOrders);
                _timerCheckOrders.Interval = TimeSpan.FromMilliseconds(timerMs2);

                _timerCheckOrders.Start();

                (sender as Button).Content = "Stop";
            }
            else
            {
                _timer.Stop();
                _timerCheckOrders.Stop();
                (sender as Button).Content = "Start";
            }
        }

        void fillGlobalVar()
        {
            timerMs1 = int.Parse(secOrdertxtbx.Text) * 1000;
            if (timerMs1 < 2000) timerMs1 = 2000;
            timerMs2 = int.Parse(secCleartxtbx.Text) * 1000;
            if (timerMs2 < 10000) timerMs2 = 10000;

            key = apiKeytxtbx.Text;
            secret = apiSecrettxtBox.Text;

            minusRateSell = decimal.Parse(sellMinusRatetxtbx.Text, CultureInfo.InvariantCulture) / 100m;
            plusRateSell = decimal.Parse(sellPlusRatetxtbx.Text, CultureInfo.InvariantCulture) / 100m;
            minusRateBuy = decimal.Parse(buyMinusRatetxtbx.Text, CultureInfo.InvariantCulture) / 100m;
            plusRateBuy = decimal.Parse(buyPlusRatetxtbx.Text, CultureInfo.InvariantCulture) / 100m;

            orderRateSpreadBuy = double.Parse(buySpreadtxtbx.Text, CultureInfo.InvariantCulture) / 100;
            orderRateSpreadSell = double.Parse(sellSpreadtxtbx.Text, CultureInfo.InvariantCulture) / 100;
            orderBuyFinances = double.Parse(desiredBuytxtbx.Text, CultureInfo.InvariantCulture) / 100;
            orderSellFinances = double.Parse(desiredSelltxtbx.Text, CultureInfo.InvariantCulture) / 100;

            maxBlockedFinances = double.Parse(maxBlockedFinancestxtbx.Text, CultureInfo.InvariantCulture) / 100;
        }

        void _timer_Elapsed(object sender, EventArgs e)
        {
            workWithApi();
        }

        void _timer_Elapsed_checkOrders(object sender, EventArgs e)
        {
            checkOrdersApi();
        }

        void checkOrdersApi()
        {
            try
            {

            HashAlgorithm algorithm = new SHA512Managed();
            string hashedSecret = System.Text.Encoding.ASCII.GetString(algorithm.ComputeHash(System.Text.Encoding.ASCII.GetBytes(secret)));

            Random r = new Random();
            int nonce = r.Next(int.MinValue, int.MaxValue);


            Dictionary<string, string> dict = new Dictionary<string, string>()
            {
                { "nonce", nonce.ToString() },
                { "MethodName", "getMyOrderList" },
            };


            string response = Query(dict, key, hashedSecret);

            JavaScriptSerializer ser = new JavaScriptSerializer();

            Dictionary<string, Dictionary<string, object>> responseDict;

            try
            {
                responseDict = ser.Deserialize<Dictionary<string, Dictionary<string, object>>>(response);
            }
            catch
            {
                lastActionLabel.Content = "Неудачная попытка авторизации\n" + DateTime.Now;
                Log("\nНеудачная попытка авторизации");
                errLabel.Content = DateTime.Now + "\n" + response;
                Log(response + "\n");
                return;
            }

            decimal minusmodSell = 1m - minusRateSell;
            decimal plusmodSell = 1m + plusRateSell;
            decimal minusmodBuy = 1m - minusRateBuy;
            decimal plusmodBuy = 1m + plusRateBuy;

            List<string> ordersToCancel = new List<string>();

            var depth3 = BtceApiV3.GetDepth(new BtcePair[] { BtcePair.btc_rur }, 1);

            decimal orderSumRub = 0m, orderSumBtc = 0m;

            foreach (string orderId in responseDict.Keys)
            {

                if (responseDict[orderId]["Type"] as string == "buy")
                {
                    if (depth3[BtcePair.btc_rur].Asks[0].Price * minusmodBuy > Decimal.Parse(responseDict[orderId]["Rate"] as string, CultureInfo.InvariantCulture) || depth3[BtcePair.btc_rur].Asks[0].Price * plusmodBuy < Decimal.Parse(responseDict[orderId]["Rate"] as string, CultureInfo.InvariantCulture))
                        ordersToCancel.Add(orderId);
                    else
                        orderSumRub += Decimal.Parse(responseDict[orderId]["Total"] as string, CultureInfo.InvariantCulture);
                }
                else
                {
                    if (depth3[BtcePair.btc_rur].Bids[0].Price * plusmodSell < Decimal.Parse(responseDict[orderId]["Rate"] as string, CultureInfo.InvariantCulture) || depth3[BtcePair.btc_rur].Bids[0].Price * minusmodSell > Decimal.Parse(responseDict[orderId]["Rate"] as string, CultureInfo.InvariantCulture))
                        ordersToCancel.Add(orderId);
                    else
                        orderSumBtc += Decimal.Parse(responseDict[orderId]["Total"] as string, CultureInfo.InvariantCulture);
                }
            }

            nonce = r.Next(int.MinValue, int.MaxValue);


            dict = new Dictionary<string, string>()
            {
                { "nonce", nonce.ToString() },
                { "MethodName", "getMyInfo" },
            };

            response = Query(dict, key, hashedSecret);

            Dictionary<string, object> responseDict1 = new Dictionary<string, object>();

            try
            {
                responseDict1 = ser.Deserialize<Dictionary<string, object>>(response);
            }
            catch
            {
                lastActionLabel.Content = "Неудачная попытка авторизации\n" + DateTime.Now;
                Log("\nНеудачная попытка авторизации");
                errLabel.Content = DateTime.Now + "\n" + response;
                Log(response + "\n");
                return;
            }

            decimal rubAmount = Decimal.Parse(responseDict1["Rub"] as string, CultureInfo.InvariantCulture);
            decimal btcAmount = Decimal.Parse(responseDict1["Btc"] as string, CultureInfo.InvariantCulture);


            double percentInOrders = (double)(orderSumRub / (rubAmount + orderSumRub));

            if (percentInOrders > maxBlockedFinances)
            {
                blockRub = true;
            }
            else
            {
                blockRub = false;
            }
            double percentInOrders1 = (double)(orderSumBtc / (btcAmount + orderSumBtc));

            if (percentInOrders1 > maxBlockedFinances)
            {
                blockBtc = true;
            }
            else
            {
                blockBtc = false;
            }


                

            foreach (string idToCancel in ordersToCancel)
            {
                nonce = r.Next(int.MinValue, int.MaxValue);

                dict = new Dictionary<string, string>()
                {
                    { "nonce", nonce.ToString() },
                    { "MethodName", "cancelOrder" },
                    { "id", idToCancel },
                };

                response = Query(dict, key, hashedSecret);

                if (response.Contains("Успешно"))
                {
                    lastActionLabel.Content = "отмена " + responseDict[idToCancel]["Type"] + "|успех\n" + DateTime.Now;
                    Log("отмена " + responseDict[idToCancel]["Type"] + "|успех");
                }
                else
                {
                    lastActionLabel.Content = "отмена " + responseDict[idToCancel]["Type"] + "|провал\n" + DateTime.Now;
                    Log(("\nотмена " + responseDict[idToCancel]["Type"] + "|провал"));
                    errLabel.Content = DateTime.Now + "\n" + response;
                    Log(response + "\n");
                }
            }

            
            }
            catch(Exception ex)
            { 
                    lastActionLabel.Content = "ошибка " + ex.Message + "\n" + DateTime.Now;
                    Log("\nошибка " + ex.Message);
                    errLabel.Content = DateTime.Now + "\n" + ex.Message;
                    Log(ex.Message + "\n");
            }

        }

        void workWithApi()
        {
            try
            {



                    HashAlgorithm algorithm = new SHA512Managed();
                    string hashedSecret = System.Text.Encoding.ASCII.GetString(algorithm.ComputeHash(System.Text.Encoding.ASCII.GetBytes(secret)));
                    

                    Random r = new Random();
                    int nonce = r.Next(int.MinValue, int.MaxValue);
                    

                    Dictionary<string, string> dict = new Dictionary<string, string>()
                    {
                        { "nonce", nonce.ToString() },
                        { "MethodName", "getMyInfo" },
                    };

                    string response = Query(dict, key, hashedSecret);

                    JavaScriptSerializer ser = new JavaScriptSerializer();

                    Dictionary<string, object> responseDict = new Dictionary<string, object>();

                    try
                    {
                        responseDict = ser.Deserialize<Dictionary<string, object>>(response);
                    }
                    catch
                    {
                        lastActionLabel.Content = "Неудачная попытка авторизации\n" + DateTime.Now;
                        Log("\nНеудачная попытка авторизации");
                        errLabel.Content = DateTime.Now + "\n" + response;
                        Log(response + "\n");
                        return;
                    }

                    decimal rubAmount = Decimal.Parse(responseDict["Rub"] as string, CultureInfo.InvariantCulture);
                    decimal btcAmount = Decimal.Parse(responseDict["Btc"] as string, CultureInfo.InvariantCulture);

                    nonce = r.Next(int.MinValue, int.MaxValue);
                    string type = (nonce < 0) ? "buy" : "sell";
                    decimal rate;


                    decimal amount;
                                    
                    if(blockRub && blockBtc)
                    {
                        lastActionLabel.Content = "Превышен установленный лимит средств в заявках, ожидаю сделок или следующей чистки\n" + DateTime.Now;
                        Log("Превышен установленный лимит средств в заявках, ожидаю сделок или следующей чистки");
                        return;
                    }

                    if(blockBtc)
                    {
                        type = "buy";
                    }
                    else if(blockRub)
                    {
                        type = "sell";
                    }


                    if (type == "buy")
                    {
                        double rand = r.NextDouble();

                        if (rand < 0.5)
                        {
                            rand = 1 - rand;
                        }

                        decimal mod = 1m - (decimal)(rand * orderRateSpreadBuy);
                        var depth3 = BtceApiV3.GetDepth(new BtcePair[] { BtcePair.btc_rur }, 1);
                        rate = decimal.Round(depth3[BtcePair.btc_rur].Bids[0].Price * mod, 8);
                        amount = decimal.Round((decimal)(r.NextDouble() * orderBuyFinances) * rubAmount / rate, 8);
                    }
                    else
                    {
                        double rand = r.NextDouble();

                        if (rand < 0.5)
                        {
                            rand = 1 - rand;
                        }

                        decimal mod = 1m + (decimal)(rand * orderRateSpreadSell);
                        var depth3 = BtceApiV3.GetDepth(new BtcePair[] { BtcePair.btc_rur }, 1);
                        rate = decimal.Round(depth3[BtcePair.btc_rur].Asks[0].Price * mod, 8);
                        amount = decimal.Round((decimal)(r.NextDouble() * orderSellFinances) * btcAmount, 8);
                    }


                    dict = new Dictionary<string, string>()
                    {
                        { "nonce", nonce.ToString() },
                        { "MethodName", "trade" },
                        { "amount",  amount.ToString(CultureInfo.InvariantCulture)},
                        { "rate",  rate.ToString(CultureInfo.InvariantCulture)},
                        { "type",  type  },
                        { "variant",  "limitorder" },
                    };

                    response = Query(dict, key, hashedSecret);


                    if (response.Contains("Успешно"))
                    {
                        lastActionLabel.Content = type + "|" + amount.ToString(CultureInfo.InvariantCulture) + "|" + rate.ToString(CultureInfo.InvariantCulture) + "руб" + "|успех\n" + DateTime.Now;
                        Log("\n" + type + "|" + amount.ToString(CultureInfo.InvariantCulture) + "|" + rate.ToString(CultureInfo.InvariantCulture) + "руб" + "|успех");
                    }
                    else
                    {
                        lastActionLabel.Content = type + "|" + amount.ToString(CultureInfo.InvariantCulture) + "|" + rate.ToString(CultureInfo.InvariantCulture) + "руб" + "|провал\n" + DateTime.Now;
                        Log("\n" + type + "|" + amount.ToString(CultureInfo.InvariantCulture) + "|" + rate.ToString(CultureInfo.InvariantCulture) + "руб" + "|провал");
                        errLabel.Content = DateTime.Now + "\n" + response;
                        Log(response + "\n");
                    }



            }
            catch (Exception ex)
            {
                lastActionLabel.Content = "ошибка " + ex.Message + "\n" + DateTime.Now;
                Log("\nошибка " + ex.Message);
                errLabel.Content = DateTime.Now + "\n" + ex.Message;
                Log(ex.Message + "\n");
            }
        }

        string ComputeHash(string hashedPassword, string message)
        {
            var key = System.Text.Encoding.ASCII.GetBytes(hashedPassword);
            string hashString;

            using (var hmac = new System.Security.Cryptography.HMACSHA512(key))
            {
                var hash = hmac.ComputeHash(System.Text.Encoding.ASCII.GetBytes(message));
                hashString = Convert.ToBase64String(hash);
            }

            return hashString;
        }

        string Query(Dictionary<string, string> args, string key, string hashedSecret)
        {
            string dataStr = ConstructMessage(args);
            var data = Encoding.ASCII.GetBytes(dataStr);

            var request = WebRequest.Create(new Uri("https://btc-up.com/api")) as HttpWebRequest;

            if (request == null)
                throw new Exception("Non HTTP WebRequest");


            request.Method = "POST";
            request.Timeout = 15000;
            request.ContentType = "application/x-www-form-urlencoded";
            request.ContentLength = data.Length;


            request.Headers.Add("Key", key);
            request.Headers.Add("Sign", ComputeHash(hashedSecret, dataStr));


            var reqStream = request.GetRequestStream();
            reqStream.Write(data, 0, data.Length);
            reqStream.Close();
            return new StreamReader(request.GetResponse().GetResponseStream()).ReadToEnd();
        }

        string ConstructMessage(Dictionary<string, string> args)
        {
            System.Text.StringBuilder msg = new System.Text.StringBuilder();

            foreach (string sentVarName in args.Keys)
            {
                msg.AppendFormat("{0}={1}", sentVarName, HttpUtility.UrlEncode(args[sentVarName]));
                msg.Append("&");
            }
            if (msg.Length != 0)
                if ((msg[msg.Length - 1]) == '&') msg.Remove(msg.Length - 1, 1);

            return msg.ToString();
        }

        private void txtbx_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timerCheckOrders.Stop();
                launchbtn.Content = "Start";
            }
        }

        public void Log(string str)
        {
            StreamWriter fileWritter = File.AppendText(System.Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "\\Log.txt");
            fileWritter.WriteLine(DateTime.Now.ToString() + " " + str);
            fileWritter.Close();
        }
    }
}
