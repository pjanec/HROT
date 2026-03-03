# sample.IsValid issue

Some places processing dds samples check sample.IsValid even before testing the instance state.
because disposal sample have sample.IsValid==false, the disposal migh not be detected at all!

