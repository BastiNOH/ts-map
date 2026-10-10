using System;
using System.Collections.Generic;
using System.Globalization;

namespace TsMap.Map
{
    /// <summary>
    /// Städte mit echten Koordinaten (Breite, Länge) für <see cref="GeoCorrection"/>.
    /// Schlüssel: Länder-Token aus der Spieldatei und englischer Stadtname (Cities.json: Country, LocalizedNames.en_gb).
    /// Städte, die es im Spiel/in den Mods nicht gibt, werden einfach übersprungen.
    /// </summary>
    public static class GeoAnchors
    {
        public static Dictionary<string, (double lat, double lon)> Ets2 => Parse(Ets2Data);

        private static Dictionary<string, (double lat, double lon)> Parse(string data)
        {
            var result = new Dictionary<string, (double, double)>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in data.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var country = line.Substring(0, line.IndexOf(':'));
                foreach (var entry in line.Substring(country.Length + 1).Split('|'))
                {
                    var parts = entry.Split(';');
                    if (parts.Length != 3) continue;
                    result[Key(country.Trim(), parts[0].Trim())] = (
                        double.Parse(parts[1], CultureInfo.InvariantCulture),
                        double.Parse(parts[2], CultureInfo.InvariantCulture));
                }
            }
            return result;
        }

        public static string Key(string country, string city) => country + "/" + city;

        // land: Stadt;Breite;Länge | ...
        private const string Ets2Data = @"
uk: London;51.507;-0.128|Birmingham;52.486;-1.890|Manchester;53.481;-2.243|Liverpool;53.408;-2.991|Newcastle upon Tyne;54.978;-1.617|Edinburgh;55.953;-3.189|Glasgow;55.864;-4.252|Aberdeen;57.150;-2.094|Inverness;57.478;-4.224|Plymouth;50.376;-4.143|Dover;51.128;1.313|Cardiff;51.481;-3.179|Felixstowe;51.961;1.351|Carlisle;54.893;-2.933|Holyhead;53.309;-4.633|Ullapool;57.896;-5.160|Exeter;50.718;-3.534|Southampton;50.910;-1.404|Portsmouth;50.805;-1.087|Norwich;52.630;1.297|Cambridge;52.205;0.122|Hull;53.745;-0.337|Grimsby;53.567;-0.081|Sheffield;53.381;-1.470|Swansea;51.621;-3.944|Aberystwyth;52.415;-4.083|Fishguard;51.994;-4.976|Oban;56.415;-5.471|Fort William;56.820;-5.105|Perth;56.396;-3.437|Thurso;58.594;-3.527|Wick;58.438;-3.093|Kirkwall;58.981;-2.960|Stornoway;58.209;-6.387|Portree;57.412;-6.196|Stranraer;54.903;-5.024|Cairnryan;54.973;-5.023|Dumfries;55.070;-3.605|Heysham;54.033;-2.912|Fraserburgh;57.693;-2.005|Bodmin;50.471;-4.718|Canterbury;51.280;1.079|Folkestone;51.081;1.166|Newport;51.588;-2.998|Chelmsford;51.736;0.469|Porthmadog;52.928;-4.133
nireland: Belfast;54.597;-5.930|Londonderry/Derry;54.997;-7.309|Larne;54.851;-5.823|Newry;54.176;-6.338
ireland: Dublin;53.350;-6.260|Galway;53.271;-9.057|Limerick;52.668;-8.630|Sligo;54.277;-8.476|Waterford;52.259;-7.110|Wexford;52.336;-6.463|Kilkenny;52.654;-7.244
iom: Douglas;54.150;-4.482
guernsey: Saint Peter Port;49.455;-2.536
jersey: Saint Helier;49.186;-2.107
france: Calais;50.951;1.858|Paris;48.857;2.352|Lille;50.629;3.057|Le Havre;49.494;0.108|Brest;48.390;-4.486|Rennes;48.117;-1.678|Nantes;47.218;-1.554|Bordeaux;44.838;-0.579|Toulouse;43.605;1.444|Marseille;43.296;5.370|Nice;43.710;7.262|Lyon;45.764;4.836|Strasbourg;48.573;7.752|Metz;49.119;6.176|Dijon;47.322;5.041|Limoges;45.834;1.262|Clermont-Ferrand;45.778;3.087|Montpellier;43.611;3.877|Perpignan;42.699;2.895|Bayonne;43.493;-1.475|La Rochelle;46.160;-1.151|Reims;49.258;4.032|Orléans;47.903;1.909|Ajaccio;41.919;8.739|Bastia;42.697;9.451|Roscoff;48.727;-3.985|Le Mans;48.006;0.199|Tours;47.394;0.685
belgium: Brussels;50.850;4.352|Antwerp;51.219;4.402|Liège;50.633;5.567
netherlands: Amsterdam;52.370;4.895|Rotterdam;51.924;4.478|Groningen;53.219;6.567|Eindhoven;51.441;5.470|IJmuiden;52.459;4.616
luxembourg: Luxembourg;49.611;6.130
germany: Berlin;52.520;13.405|Hamburg;53.551;9.994|Munich;48.137;11.576|Cologne;50.938;6.960|Frankfurt am Main;50.110;8.682|Stuttgart;48.776;9.183|Dresden;51.050;13.738|Leipzig;51.340;12.375|Nuremberg;49.452;11.077|Hanover;52.376;9.732|Bremen;53.079;8.802|Kiel;54.323;10.123|Rostock;54.092;12.099|Kassel;51.312;9.479|Erfurt;50.978;11.029|Dortmund;51.514;7.468|Magdeburg;52.120;11.628|Mannheim;49.488;8.466|Osnabrück;52.279;8.047|Würzburg;49.792;9.953
switzerland: Bern;46.948;7.447|Zürich;47.377;8.540|Geneva;46.204;6.143
austria: Vienna;48.208;16.373|Graz;47.071;15.440|Linz;48.306;14.286|Salzburg;47.809;13.055|Innsbruck;47.269;11.404|Klagenfurt am Wörthersee;46.625;14.305
italy: Rome;41.903;12.496|Milan;45.464;9.190|Turin;45.070;7.687|Venice;45.441;12.316|Genoa;44.405;8.946|Bologna;44.494;11.343|Florence;43.770;11.256|Naples;40.852;14.268|Bari;41.117;16.872|Taranto;40.464;17.247|Palermo;38.116;13.361|Catania;37.502;15.087|Messina;38.193;15.554|Cagliari;39.224;9.122|Olbia;40.924;9.502|Sassari;40.727;8.560|Ancona;43.616;13.519|Pescara;42.461;14.216|Trieste;45.650;13.777|Verona;45.438;10.992|Catanzaro;38.910;16.588
spain: Madrid;40.417;-3.704|Barcelona;41.387;2.170|Valencia;39.470;-0.376|Seville;37.389;-5.984|Málaga;36.721;-4.421|Bilbao;43.263;-2.935|Zaragoza;41.649;-0.889|A Coruña;43.362;-8.411|Vigo;42.240;-8.721|Gijón;43.532;-5.661|Santander;43.462;-3.810|Valladolid;41.652;-4.725|Salamanca;40.970;-5.664|Granada;37.177;-3.599|Almería;36.834;-2.464|Murcia;37.992;-1.131|Algeciras;36.131;-5.454|Huelva;37.261;-6.945|Badajoz;38.879;-6.970|Albacete;38.994;-1.858|León;42.599;-5.567|Burgos;42.344;-3.697|Pamplona;42.812;-1.646|Tarragona;41.119;1.245|Lleida;41.617;0.620|Córdoba;37.888;-4.779
gibraltar: Gibraltar;36.140;-5.353
portugal: Lisbon;38.722;-9.139|Porto;41.150;-8.611|Faro;37.019;-7.930|Coimbra;40.203;-8.410|Evora;38.571;-7.909|Guarda;40.537;-7.267|Sines;37.956;-8.870|Beja;38.015;-7.863
andorra: Andorra la Vella;42.507;1.522
monaco: Monaco;43.738;7.425
denmark: Copenhagen;55.676;12.568|Aarhus;56.163;10.204|Aalborg;57.048;9.919|Esbjerg;55.476;8.459|Odense;55.403;10.402|Frederikshavn;57.441;10.537|Hirtshals;57.588;9.959|Rønne;55.101;14.706|Gedser;54.575;11.928
norway: Oslo;59.913;10.752|Bergen;60.391;5.322|Stavanger;58.970;5.733|Kristiansand;58.146;7.996|Trondheim;63.431;10.395|Ålesund;62.472;6.149|Molde;62.737;7.160|Bodø;67.280;14.405|Narvik;68.438;17.427|Tromsø;69.649;18.956|Alta;69.969;23.272|Honningsvåg;70.982;25.970|Kirkenes;69.727;30.045|Vadsø;70.074;29.750|Mo i Rana;66.313;14.142|Lillehammer;61.115;10.466|Hamar;60.795;11.068|Svolvær;68.234;14.568|Steinkjer;64.015;11.495|Kristiansund;63.110;7.728
sweden: Stockholm;59.329;18.069|Gothenburg;57.709;11.975|Malmö;55.605;13.004|Uppsala;59.859;17.639|Linköping;58.411;15.622|Jönköping;57.783;14.161|Karlstad;59.403;13.511|Örebro;59.275;15.213|Gävle;60.675;17.141|Sundsvall;62.391;17.307|Umeå;63.826;20.263|Lulea;65.584;22.155|Kiruna;67.856;20.225|Östersund;63.177;14.636|Kalmar;56.663;16.356|Karlskrona;56.161;15.587|Visby;57.634;18.295|Helsingborg;56.046;12.695|Haparanda;65.836;24.137|Skellefteå;64.750;20.950|Växjö;56.879;14.806
finland: Helsinki;60.170;24.938|Turku;60.452;22.267|Tampere;61.498;23.761|Oulu;65.012;25.468|Rovaniemi;66.503;25.729|Kuopio;62.893;27.678|Vaasa;63.096;21.616|Joensuu;62.601;29.764|Lahti;60.983;25.656|Kotka;60.467;26.946|Lappeenranta;61.058;28.188|Jyväskylä;62.242;25.747|Pori;61.485;21.797|Ivalo;68.658;27.540|Kajaani;64.227;27.728|Kuusamo;65.966;29.189|Tornio;65.848;24.146|Utsjoki;69.908;27.026|Kittilä;67.659;24.909
aland: Mariehamn;60.097;19.935
estonia: Tallinn;59.437;24.754|Tartu;58.378;26.729|Pärnu;58.385;24.497|Narva;59.377;28.190|Kuressaare;58.253;22.485
latvia: Riga;56.950;24.105|Daugavpils;55.875;26.536|Liepāja;56.505;21.011|Ventspils;57.390;21.565|Valmiera;57.541;25.427|Rēzekne;56.510;27.340
lithuania: Vilnius;54.687;25.280|Kaunas;54.899;23.904|Klaipėda;55.704;21.144|Šiauliai;55.934;23.316|Panevėžys;55.737;24.370
poland: Warsaw;52.230;21.012|Kraków;50.065;19.945|Gdańsk;54.352;18.646|Poznań;52.406;16.925|Wrocław;51.108;17.039|Szczecin;53.429;14.553|Łódź;51.759;19.456|Lublin;51.246;22.568|Bialystok;53.133;23.169|Rzeszów;50.041;21.999|Olsztyn;53.778;20.480|Katowice;50.265;19.024|Bydgoszcz;53.123;18.008|Koszalin;54.194;16.172|Suwałki;54.111;22.931|Przemyśl;49.784;22.768|Kielce;50.866;20.628|Świnoujście;53.910;14.247
czech: Prague;50.076;14.438|Brno;49.195;16.608|Ostrava;49.820;18.262|Olomouc;49.594;17.251|Jihlava;49.396;15.591
slovakia: Bratislava;48.149;17.107|Košice;48.716;21.261|Banská Bystrica;48.736;19.146
hungary: Budapest;47.498;19.040|Debrecen;47.531;21.627|Szeged;46.253;20.141|Pécs;46.073;18.232|Győr;47.687;17.650
slovenia: Ljubljana;46.057;14.506|Maribor;46.554;15.646|Koper;45.548;13.730
croatia: Zagreb;45.815;15.982|Split;43.508;16.440|Rijeka;45.327;14.442|Osijek;45.555;18.696|Dubrovnik;42.651;18.094|Zadar;44.119;15.231
bosnia: Sarajevo;43.856;18.413|Banja Luka;44.772;17.191|Mostar;43.343;17.808|Tuzla;44.538;18.676
serbia: Belgrade;44.787;20.457|Niš;43.320;21.896|Novi Sad;45.267;19.833|Subotica;46.100;19.667
montenegro: Podgorica;42.441;19.263|Bar;42.093;19.100
kosovo: Pristina;42.663;21.165
macedonia: Skopje;41.998;21.425|Bitola;41.031;21.334
albania: Tirana;41.328;19.819|Durrës;41.323;19.441|Vlorë;40.466;19.490
greece: Athens;37.984;23.728|Thessaloniki;40.640;22.944|Patras;38.246;21.735|Heraklion;35.339;25.144|Chania;35.514;24.018|Ioannina;39.665;20.853|Larissa;39.639;22.419|Kalamata;37.039;22.114|Kavala;40.937;24.413|Rhodes;36.434;28.217|Mitilini;39.105;26.555|Chios;38.368;26.136
bulgaria: Sofia;42.698;23.322|Plovdiv;42.136;24.745|Varna;43.214;27.915|Burgas;42.505;27.463|Ruse;43.836;25.966|Vidin;43.990;22.882|Pleven;43.417;24.607
romania: Bucharest;44.426;26.103|Cluj-Napoca;46.771;23.624|Timișoara;45.748;21.209|Iași;47.159;27.602|Constanța;44.160;28.635|Brașov;45.658;25.601|Craiova;44.330;23.795|Galați;45.435;28.008|Suceava;47.651;26.256|Sibiu;45.793;24.152|Baia Mare;47.657;23.568|Mangalia;43.817;28.583
moldova: Chişinău;47.011;28.864|Bălţi;47.762;27.929|Tiraspol;46.848;29.596
ukraine: Kyiv;50.450;30.523|Lviv;49.840;24.030|Odesa;46.482;30.723|Kharkiv;49.993;36.230|Dnipro;48.464;35.046|Donetsk;48.015;37.803|Zaporizhzhia;47.838;35.139|Mariupol;47.097;37.543|Mykolaiv;46.975;31.995|Kherson;46.636;32.617|Poltava;49.588;34.551|Chernihiv;51.498;31.289|Sumy;50.907;34.798|Uzhhorod;48.621;22.288|Chernivtsi;48.292;25.936|Rivne;50.620;26.252|Lutsk;50.747;25.325|Luhansk;48.574;39.308|Zhytomyr;50.254;28.658|Vinnytsia;49.233;28.468|Uman;48.749;30.222|Kryvyi Rih;47.910;33.392|Melitopol;46.849;35.367|Berdyansk;46.757;36.799|Ternopil;49.553;25.595
belarus: Minsk;53.902;27.562|Brest;52.098;23.734|Grodno;53.678;23.830|Gomel;52.441;30.988|Vitebsk;55.184;30.202|Mahilyow;53.894;30.331|Pinsk;52.112;26.103|Polotsk;55.486;28.786|Orsha;54.509;30.426|Bobruisk;53.140;29.222
russia: Moscow;55.756;37.617|Saint Petersburg;59.939;30.316|Kaliningrad;54.710;20.452|Murmansk;68.970;33.075|Petrozavodsk;61.785;34.346|Pskov;57.819;28.332|Veliky Novgorod;58.522;31.270|Tver;56.859;35.912|Smolensk;54.783;32.045|Bryansk;53.243;34.364|Kursk;51.730;36.193|Belgorod;50.595;36.587|Voronezh;51.661;39.200|Tula;54.193;37.618|Kaluga;54.514;36.262|Orel;52.970;36.064|Tambov;52.721;41.452|Penza;53.195;45.018|Saratov;51.534;46.034|Engels;51.485;46.126|Volgograd;48.708;44.513|Vyborg;60.710;28.749|Kandalaksha;67.157;32.412|Apatity;67.568;33.393|Monchegorsk;67.939;32.915|Sortavala;61.704;30.690|Tikhvin;59.645;33.529|Velikiye Luki;56.341;30.529|Rzhev;56.262;34.329|Vyazma;55.211;34.296|Kolomna;55.079;38.778|Klin;56.334;36.731|Luga;58.737;29.846|Kingisepp;59.374;28.611|Kem;64.955;34.579|Segezha;63.744;34.312|Medvezhyegorsk;62.912;34.457|Nikel;69.411;30.226|Kola;68.879;33.023|Simferopol;44.952;34.102|Sevastopol;44.616;33.525|Yalta;44.495;34.166|Feodosia;45.032;35.382|Ростов-на-Дону;47.236;39.714|Краснодар;45.035;38.975|Новороссийск;44.723;37.769|Сочи;43.585;39.723|Ставрополь;45.044;41.969|Майкоп;44.609;40.106|Анапа;44.895;37.316|Туапсе;44.098;39.073|Ейск;46.711;38.274|Армавир;44.999;41.130
kazakhstan: Aktau;43.650;51.160|Atyrau;47.094;51.924|Oral;51.233;51.367|Aktobe;50.283;57.167|Kostanay;53.214;63.632|Rudny;52.966;63.120|Zhanaozen;43.341;52.861|Beyneu;45.316;55.200|Kulsary;46.953;54.019|Khromtau;50.250;58.443|Lisakovsk;52.538;62.493
turkey: İstanbul;41.008;28.978|Ankara;39.934;32.860|Izmir;38.424;27.143|Bursa;40.188;29.061|Antalya;36.897;30.713|Adana;37.000;35.321|Mersin;36.812;34.641|Konya;37.874;32.493|Kayseri;38.720;35.488|Samsun;41.287;36.330|Trabzon;41.002;39.717|Erzurum;39.904;41.268|Diyarbakır;37.914;40.231|Gaziantep;37.066;37.383|Malatya;38.355;38.309|Sivas;39.748;37.016|Eskişehir;39.777;30.520|Edirne;41.677;26.556|Çanakkale;40.155;26.414|Kars;40.601;43.097|Hopa;41.391;41.432|Sinop;42.027;35.151|Antakya;36.202;36.160|Denizli;37.783;29.095|Bodrum;37.034;27.430|Fethiye;36.622;29.116|Alanya;36.544;31.996|Afyonkarahisar;38.757;30.538|Şanlıurfa;37.167;38.796|Mardin;37.313;40.735|Ordu;40.986;37.879|Zonguldak;41.456;31.799|Tatvan;38.505;42.281|Iğdır;39.920;44.045|Kastamonu;41.389;33.783|Çorum;40.550;34.954|Erzincan;39.750;39.491|Batman;37.881;41.135|Tekirdağ;40.978;27.511|Balikesir;39.648;27.883|Isparta;37.764;30.556|Silifke;36.378;33.934
georgia: Tbilisi;41.716;44.783|Batumi;41.643;41.640|Kutaisi;42.270;42.705|Poti;42.146;41.672|Zugdidi;42.509;41.871|Gori;41.985;44.112|Telavi;41.920;45.473|Rustavi;41.549;44.993|Akhaltsikhe;41.639;42.983
abkhazia: Sokhumi;43.003;41.023|Gagra;43.334;40.230
armenia: Vanadzor;40.813;44.488
azerbaijan: Mingachevir;40.765;47.060|Yevlakh;40.618;47.150|Zaqatala;41.633;46.643|Alat;39.950;49.410|Kurdamir;40.343;48.162|Hajigabul;40.039;48.920
cyprus: Nicosia;35.185;33.382|Limassol;34.707;33.022|Larnaca;34.917;33.634|Paphos;34.776;32.424
lebanon: Beirut;33.894;35.502|Tripoli;34.437;35.850|Sidon;33.563;35.371|Tyre;33.271;35.196|Baalbek;34.006;36.218|Zahlé;33.847;35.902|Byblos;34.121;35.648
syria: Damascus;33.513;36.276|Latakia;35.532;35.791|Tartus;34.889;35.887|Deraa;32.619;36.102
israel: Tel Aviv;32.085;34.782|Jerusalem;31.769;35.216|Haifa;32.794;34.990|Eilat;29.558;34.952|Beersheba;31.252;34.791|Ashdod;31.804;34.655|Ashkelon;31.669;34.571|Netanya;32.322;34.853|Nazareth;32.700;35.304|Tiberias;32.795;35.531|Kiryat Shmona;33.208;35.570|Mitzpe Ramon;30.610;34.801|Dimona;31.069;35.033|Acre;32.927;35.082|Afula;32.608;35.289|Hadera;32.434;34.919|Ein Gedi;31.454;35.388|Beit She'an;32.497;35.497
westbank: Ramallah;31.903;35.204|Jericho;31.857;35.444|Bethlehem;31.705;35.200
jordan: Amman;31.954;35.911|Aqaba;29.532;35.006|Irbid;32.556;35.850|Zarqa;32.064;36.088|Ma'an;30.196;35.734|Al-Karak;31.185;35.705|Madaba;31.717;35.794|Mafraq;32.343;36.208|Jerash;32.281;35.899|Ruwaished;32.508;38.202|Safawi;32.200;37.130
iraq: Ar-Rutbah;33.038;40.284|Al-Qa'im;34.371;41.093|Haditha;34.140;42.378|Hit;33.638;42.827|Baiji;34.929;43.493
egypt: Port Said;31.265;32.302|Arish;31.132;33.803|Damietta;31.417;31.815|Taba;29.492;34.897
saudia: Haql;29.288;34.942
malta: Valletta;35.899;14.514
morocco: Rabat;34.020;-6.841|Tangier;35.759;-5.834|Tétouan;35.578;-5.368|Kenitra;34.261;-6.580|Larache;35.193;-6.156|Ksar El Kbir;35.002;-5.904|Salé;34.053;-6.798|Port Tanger Med;35.890;-5.500
tunisia: Tunis;36.806;10.181|Enfidha;36.135;10.381|Utique;37.056;10.062
svalbard: Longyearbyen;78.223;15.627
faroe: Tórshavn;62.009;-6.772|Klaksvík;62.226;-6.589
iceland: Seyðisfjörður;65.260;-14.010|Reyðarfjörður;65.033;-14.217|Höfn í Hornafirði;64.254;-15.212|Krafla;65.715;-16.778|Akureyri;65.683;-18.090|Varmahlíð;65.553;-19.452|Blönduós;65.660;-20.287|Norðurfjörður;66.051;-21.554|Vík í Mýrdal;63.419;-19.006|Hólmavík;65.705;-21.682|Ísafjörður;66.075;-23.135|Bolungarvík;66.157;-23.252|Vestmannaeyjar;63.442;-20.270|Selfoss;63.933;-20.997|Þorlákshöfn;63.856;-21.383|Reykjavík;64.147;-21.942|Borgarnes;64.539;-21.920|Akranes;64.321;-22.075|Stykkishólmur;65.075;-22.727|Suðureyri;66.130;-23.533|Keflavík;64.004;-22.562|Ólafsvík;64.894;-23.711
greenland: Nuuk;64.175;-51.739
";
    }
}
