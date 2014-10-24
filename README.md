ACFun-Board
===========
Acfun匿名版 Metro版本

所有的数据处理均在Thread.cs中
Get函数（已修复）：getThreadList和getReplyList
Get函数通过引用ThreadSource类来处理json

Post函数（已失效）：postReply和postThread
Post函数通过引用PostThread或PostReply类来序列化json

通过在FourmPage.xaml.cs中绑定fourm_sourse和thread_source到itemListView和itemDetail来实现数据绑定
