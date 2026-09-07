// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

#include <munit.h>
#include <chiaki/config.h>

extern MunitTest tests_seq_num[];
extern MunitTest tests_key_state[];
extern MunitTest tests_http[];
extern MunitTest tests_rpcrypt[];
extern MunitTest tests_gkcrypt[];
extern MunitTest tests_regist[];
extern MunitTest tests_bitstream[];
extern MunitTest tests_session_baseline[];
extern MunitTest tests_decoderchoice[];

/* PP760: the frame path's four suites, behind the option that builds their files.
 *
 * fec.c, frameprocessor.c, allocbudget.c and videoreceiver.c link streamconnection.c,
 * videoreceiver.c, frameprocessor.c and fec.c, which PP696 takes out of the build. An extern with
 * no definition is a link failure, so the entry point has to know both shapes before the commit
 * that changes which one it is - and that commit may not edit a test file.
 *
 * The shape is ffmpegdecoder.c's, three lines down: a conditional suite in this file is already an
 * #if around the extern and around the suites[] entry. What differs is where the macro comes from.
 * That one rides on config.h out of lib/, and a definition added there would be an edit to lib/ in
 * a commit that is not the one allowed to make it - so this comes off the test target instead,
 * where the same list that drops the four files turns it off. */
#if CHIAKI_UNIT_HAVE_FRAMEPATH
extern MunitTest tests_fec[];
extern MunitTest tests_frame_processor[];
extern MunitTest tests_video_receiver[];
/* PP786: allocbudget.c is claimed by BOTH flips, so it is compiled only while both are on.
 *
 * It links frameprocessor.c and videoreceiver.c, which PP696 removes, AND it calls
 * chiaki_takion_v9_av_packet_parse and takion_handle_packet_av, which PP27's end state removes -
 * PP780's link named it under both. So the file leaves on whichever deletion lands first, and a
 * guard naming only one of them would leave an extern with no definition on the other day. */
#if CHIAKI_UNIT_HAVE_TAKION
extern MunitTest tests_alloc_budget[];
#endif
#endif
/* PP786: takion's own two suites, behind the option that builds their files.
 *
 * PP760 did this for the frame path's four and takion's three had none, so PP27's deletion would
 * have taken nine munit cases out of the run and the floor would have fallen with nothing saying it
 * should. takion.c and reorderqueue.c exercise the modules directly - PP780's link names seven
 * symbols in one and six in the other - so they go with the C they test. */
#if CHIAKI_UNIT_HAVE_TAKION
extern MunitTest tests_reorder_queue[];
extern MunitTest tests_takion[];
#endif
#if CHIAKI_LIB_ENABLE_FFMPEG_DECODER
extern MunitTest tests_ffmpegdecoder[];
#endif

static MunitSuite suites[] = {
	{
		"/seq_num",
		tests_seq_num,
		NULL,
		1,
		MUNIT_SUITE_OPTION_NONE
	},
	{
		"/key_state",
		tests_key_state,
		NULL,
		1,
		MUNIT_SUITE_OPTION_NONE
	},
#if CHIAKI_UNIT_HAVE_TAKION
	{
		"/reorder_queue",
		tests_reorder_queue,
		NULL,
		1,
		MUNIT_SUITE_OPTION_NONE
	},
#endif
	{
		"/http",
		tests_http,
		NULL,
		1,
		MUNIT_SUITE_OPTION_NONE
	},
	{
		"/rpcrypt",
		tests_rpcrypt,
		NULL,
		1,
		MUNIT_SUITE_OPTION_NONE
	},
	{
		"/gkcrypt",
		tests_gkcrypt,
		NULL,
		1,
		MUNIT_SUITE_OPTION_NONE
	},
#if CHIAKI_UNIT_HAVE_TAKION
	{
		"/takion",
		tests_takion,
		NULL,
		1,
		MUNIT_SUITE_OPTION_NONE
	},
#endif
#if CHIAKI_UNIT_HAVE_FRAMEPATH
	{
		"/fec",
		tests_fec,
		NULL,
		1,
		MUNIT_SUITE_OPTION_NONE
	},
#endif
	{
		"/regist",
		tests_regist,
		NULL,
		1,
		MUNIT_SUITE_OPTION_NONE
	},
	{
		"/bitstream",
		tests_bitstream,
		NULL,
		1,
		MUNIT_SUITE_OPTION_NONE
	},
	{
		"/session_baseline",
		tests_session_baseline,
		NULL,
		1,
		MUNIT_SUITE_OPTION_NONE
	},
#if CHIAKI_UNIT_HAVE_FRAMEPATH
	{
		"/frame_processor",
		tests_frame_processor,
		NULL,
		1,
		MUNIT_SUITE_OPTION_NONE
	},
#if CHIAKI_UNIT_HAVE_TAKION
	{
		"/alloc_budget",
		tests_alloc_budget,
		NULL,
		1,
		MUNIT_SUITE_OPTION_NONE
	},
#endif
	{
		"/video_receiver",
		tests_video_receiver,
		NULL,
		1,
		MUNIT_SUITE_OPTION_NONE
	},
#endif
	{
		"/decoderchoice",
		tests_decoderchoice,
		NULL,
		1,
		MUNIT_SUITE_OPTION_NONE
	},
#if CHIAKI_LIB_ENABLE_FFMPEG_DECODER
	{
		"/ffmpegdecoder",
		tests_ffmpegdecoder,
		NULL,
		1,
		MUNIT_SUITE_OPTION_NONE
	},
#endif
	{ NULL, NULL, NULL, 0, MUNIT_SUITE_OPTION_NONE }
};

static const MunitSuite suite_main = {
	"/chiaki",
	NULL,
	suites,
	1,
	MUNIT_SUITE_OPTION_NONE
};

int main(int argc, char *argv[])
{
	return munit_suite_main(&suite_main, NULL, argc, argv);
}
