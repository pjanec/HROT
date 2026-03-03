# sample.IsValid issue

Some places processing dds samples check sample.IsValid even before testing the instance state.
because disposal sample have sample.IsValid==false, the disposal migh not be detected at all!

# sample.Data does not throw from disposal samples

Some older implementation of cyclone dds had a bug where sample.Data was throwing exception
if sample.IsValid==fasle


# context menu
works just once per entity click - second time no menu