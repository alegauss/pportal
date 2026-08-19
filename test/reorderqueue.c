// SPDX-License-Identifier: LicenseRef-AGPL-3.0-only-OpenSSL

#include <munit.h>

#include <chiaki/reorderqueue.h>

#define DROP_RECORD_MAX 16

typedef struct drop_record_t
{
	uint64_t count[DROP_RECORD_MAX];
	uint64_t seq_num[DROP_RECORD_MAX];
	bool failed;
} DropRecord;

static void drop(uint64_t seq_num, void *elem_user, void *cb_user)
{
	DropRecord *record = cb_user;
	uint64_t v = (uint64_t)(size_t)elem_user;
	if(v > DROP_RECORD_MAX)
	{
		record->failed = true;
		return;
	}
	record->count[v]++;
	record->seq_num[v] = seq_num;
}

static MunitResult test_reorder_queue_16(const MunitParameter params[], void *test_user)
{
	ChiakiReorderQueue queue;
	ChiakiErrorCode err = chiaki_reorder_queue_init_16(&queue, 2, 42);
	munit_assert_int(err, ==, CHIAKI_ERR_SUCCESS);
	munit_assert_size(chiaki_reorder_queue_size(&queue), ==, 4);
	munit_assert_uint64(chiaki_reorder_queue_count(&queue), ==, 0);

	chiaki_reorder_queue_set_drop_strategy(&queue, CHIAKI_REORDER_QUEUE_DROP_STRATEGY_END);

	DropRecord drop_record = { 0 };
	chiaki_reorder_queue_set_drop_cb(&queue, drop, &drop_record);

	uint64_t seq_num = 0;
	void *user = NULL;

	// pull from empty
	bool pulled = chiaki_reorder_queue_pull(&queue, &seq_num, &user);
	munit_assert(!pulled);
	munit_assert_uint64(chiaki_reorder_queue_count(&queue), ==, 0);
	munit_assert(!drop_record.failed);

	// push one
	chiaki_reorder_queue_push(&queue, 42, (void *)0);
	munit_assert_uint64(chiaki_reorder_queue_count(&queue), ==, 1);

	// pull one
	pulled = chiaki_reorder_queue_pull(&queue, &seq_num, &user);
	munit_assert(pulled);
	munit_assert_uint64(chiaki_reorder_queue_count(&queue), ==, 0);
	munit_assert(!drop_record.failed);
	munit_assert_uint64(drop_record.count[0], ==, 0);
	munit_assert_uint64((uint64_t)(size_t)user, ==, 0);
	munit_assert_uint64(seq_num, ==, 42);

	// push outdated
	chiaki_reorder_queue_push(&queue, 42, (void *)0);
	munit_assert_uint64(chiaki_reorder_queue_count(&queue), ==, 0);
	munit_assert(!drop_record.failed);
	munit_assert_uint64(drop_record.count[0], ==, 1);
	munit_assert_uint64(drop_record.seq_num[0], ==, 42);
	memset(&drop_record, 0, sizeof(drop_record));

	// push until full out of order and try to pull in between
	chiaki_reorder_queue_push(&queue, 46, (void *)1);
	pulled = chiaki_reorder_queue_pull(&queue, &seq_num, &user);
	munit_assert(!pulled);
	chiaki_reorder_queue_push(&queue, 45, (void *)2);
	pulled = chiaki_reorder_queue_pull(&queue, &seq_num, &user);
	munit_assert(!pulled);
	chiaki_reorder_queue_push(&queue, 44, (void *)3);
	pulled = chiaki_reorder_queue_pull(&queue, &seq_num, &user);
	munit_assert(!pulled);
	chiaki_reorder_queue_push(&queue, 43, (void *)4);
	munit_assert(!drop_record.failed);
	for(size_t i=0; i<DROP_RECORD_MAX; i++)
		munit_assert_uint64(drop_record.count[i], ==, 0);

	// push more, because of CHIAKI_REORDER_QUEUE_DROP_STRATEGY_END this should be dropped
	chiaki_reorder_queue_push(&queue, 47, (void *)5);
	munit_assert(!drop_record.failed);
	for(size_t i=0; i<DROP_RECORD_MAX; i++)
		munit_assert_uint64(drop_record.count[i], ==, i == 5 ? 1 : 0);
	munit_assert_uint64(drop_record.seq_num[5], ==, 47);
	memset(&drop_record, 0, sizeof(drop_record));

	// push more with CHIAKI_REORDER_QUEUE_DROP_STRATEGY_BEGIN, so older elements should be dropped
	chiaki_reorder_queue_set_drop_strategy(&queue, CHIAKI_REORDER_QUEUE_DROP_STRATEGY_BEGIN);
	chiaki_reorder_queue_push(&queue, 47, (void *)5);
	munit_assert(!drop_record.failed);
	for(size_t i=0; i<DROP_RECORD_MAX; i++)
		munit_assert_uint64(drop_record.count[i], ==, i == 4 ? 1 : 0);
	munit_assert_uint64(drop_record.seq_num[4], ==, 43);
	memset(&drop_record, 0, sizeof(drop_record));
	
	// pull all, elements should arrive in order
	pulled = chiaki_reorder_queue_pull(&queue, &seq_num, &user);
	munit_assert(pulled);
	munit_assert_uint64(seq_num, ==, 44);
	munit_assert_uint64((uint64_t)(size_t)user, ==, 3);

	pulled = chiaki_reorder_queue_pull(&queue, &seq_num, &user);
	munit_assert(pulled);
	munit_assert_uint64(seq_num, ==, 45);
	munit_assert_uint64((uint64_t)(size_t)user, ==, 2);

	pulled = chiaki_reorder_queue_pull(&queue, &seq_num, &user);
	munit_assert(pulled);
	munit_assert_uint64(seq_num, ==, 46);
	munit_assert_uint64((uint64_t)(size_t)user, ==, 1);

	pulled = chiaki_reorder_queue_pull(&queue, &seq_num, &user);
	munit_assert(pulled);
	munit_assert_uint64(seq_num, ==, 47);
	munit_assert_uint64((uint64_t)(size_t)user, ==, 5);

	// should be empty now again
	pulled = chiaki_reorder_queue_pull(&queue, &seq_num, &user);
	munit_assert(!pulled);
	munit_assert_uint64(chiaki_reorder_queue_count(&queue), ==, 0);

	munit_assert(!drop_record.failed);
	for(size_t i=0; i<DROP_RECORD_MAX; i++)
		munit_assert_uint64(drop_record.count[i], ==, 0);

	// now push something much higher, because of CHIAKI_REORDER_QUEUE_DROP_STRATEGY_BEGIN, the queue should be relocated
	chiaki_reorder_queue_push(&queue, 1337, (void *)6);
	munit_assert_uint64(chiaki_reorder_queue_count(&queue), ==, 1);
	munit_assert(!drop_record.failed);
	for(size_t i=0; i<DROP_RECORD_MAX; i++)
		munit_assert_uint64(drop_record.count[i], ==, 0);

	// and pull again
	pulled = chiaki_reorder_queue_pull(&queue, &seq_num, &user);
	munit_assert(pulled);
	munit_assert_uint64(seq_num, ==, 1337);
	munit_assert_uint64((uint64_t)(size_t)user, ==, 6);
	munit_assert_uint64(chiaki_reorder_queue_count(&queue), ==, 0);

	// same as before, but with an element in the queue that will be dropped
	chiaki_reorder_queue_push(&queue, 1338, (void *)7);
	munit_assert_uint64(chiaki_reorder_queue_count(&queue), ==, 1);
	munit_assert(!drop_record.failed);
	for(size_t i=0; i<DROP_RECORD_MAX; i++)
		munit_assert_uint64(drop_record.count[i], ==, 0);

	chiaki_reorder_queue_push(&queue, 2000, (void *)8);
	munit_assert_uint64(chiaki_reorder_queue_count(&queue), ==, 1);
	munit_assert(!drop_record.failed);
	for(size_t i=0; i<DROP_RECORD_MAX; i++)
		munit_assert_uint64(drop_record.count[i], ==, i == 7 ? 1 : 0);
	munit_assert_uint64(drop_record.seq_num[7], ==, 1338);

	// pull again
	pulled = chiaki_reorder_queue_pull(&queue, &seq_num, &user);
	munit_assert(pulled);
	munit_assert_uint64(seq_num, ==, 2000);
	munit_assert_uint64((uint64_t)(size_t)user, ==, 8);
	munit_assert_uint64(chiaki_reorder_queue_count(&queue), ==, 0);

	chiaki_reorder_queue_fini(&queue);

	return MUNIT_OK;
}


/**
 * PP107: peek and drop, which nothing in this suite called until now.
 *
 * That is why what follows pins behaviour that is wrong. Both functions are reachable only from
 * takion's re-check-MACs path, both are broken there, and neither had a test to notice - so the
 * first thing to write is not a fix but the coverage that makes a fix visible.
 *
 * Every assertion below that describes a defect says so. When the decision on PP107 is taken and
 * lib/ is changed, these go red and have to be edited deliberately, which is the point: a silent
 * behaviour change in a transport is what this suite exists to prevent.
 */
static MunitResult test_reorder_queue_peek_drop(const MunitParameter params[], void *test_user)
{
	ChiakiReorderQueue queue;
	ChiakiErrorCode err = chiaki_reorder_queue_init_16(&queue, 3, 100);
	munit_assert_int(err, ==, CHIAKI_ERR_SUCCESS);

	DropRecord drop_record = { 0 };
	chiaki_reorder_queue_set_drop_cb(&queue, drop, &drop_record);

	uint64_t seq_num;
	void *user;

	// The index is an OFFSET from the queue's begin, not a sequence number. begin is 100, so the
	// element pushed as 102 is at index 2 - and index 102 is past the end of a queue of 8.
	chiaki_reorder_queue_push(&queue, 102, (void *)2);
	munit_assert_uint64(chiaki_reorder_queue_count(&queue), ==, 3);
	munit_assert(chiaki_reorder_queue_peek(&queue, 2, &seq_num, &user));
	munit_assert_uint64(seq_num, ==, 102);
	munit_assert_uint64((uint64_t)(size_t)user, ==, 2);
	munit_assert(!chiaki_reorder_queue_peek(&queue, 102, &seq_num, &user));

	// A slot inside the window that nothing has filled peeks as absent rather than as zero.
	munit_assert(!chiaki_reorder_queue_peek(&queue, 0, &seq_num, &user));
	munit_assert(!chiaki_reorder_queue_peek(&queue, 1, &seq_num, &user));

	// DEFECT (PP107): chiaki_reorder_queue_drop announces the element to the drop callback and
	// does not remove it. It never clears entry->set, so its own count-reduction loop - written
	// as `while(!entry->set)` - cannot run either. The element stays peekable, stays pullable,
	// and count is unchanged. Asserted as it is so that fixing it has to come here first.
	chiaki_reorder_queue_drop(&queue, 2);
	munit_assert(!drop_record.failed);
	munit_assert_uint64(drop_record.count[2], ==, 1);
	munit_assert_uint64(drop_record.seq_num[2], ==, 102);

	munit_assert(chiaki_reorder_queue_peek(&queue, 2, &seq_num, &user));
	munit_assert_uint64(seq_num, ==, 102);
	munit_assert_uint64(chiaki_reorder_queue_count(&queue), ==, 3);

	// …and it is still delivered. Fill the gap and the "dropped" element comes out with the rest.
	chiaki_reorder_queue_push(&queue, 100, (void *)0);
	chiaki_reorder_queue_push(&queue, 101, (void *)1);
	for(uint64_t expected = 100; expected <= 102; expected++)
	{
		munit_assert(chiaki_reorder_queue_pull(&queue, &seq_num, &user));
		munit_assert_uint64(seq_num, ==, expected);
	}
	munit_assert(!chiaki_reorder_queue_pull(&queue, &seq_num, &user));

	// Dropping an index outside the window is a no-op, which is the one thing drop does do.
	memset(&drop_record, 0, sizeof(drop_record));
	chiaki_reorder_queue_drop(&queue, 50);
	munit_assert(!drop_record.failed);
	for(size_t i=0; i<DROP_RECORD_MAX; i++)
		munit_assert_uint64(drop_record.count[i], ==, 0);

	// NOT asserted here: chiaki_reorder_queue_peek writes through its seq_num pointer without
	// checking it, and takion.c calls it with NULL. Exercising that is the crash, so it is read
	// out of the source and recorded in PP107 rather than run.

	chiaki_reorder_queue_fini(&queue);
	return MUNIT_OK;
}


MunitTest tests_reorder_queue[] = {
	{
		"/reorder_queue_16",
		test_reorder_queue_16,
		NULL,
		NULL,
		MUNIT_TEST_OPTION_NONE,
		NULL
	},
	{
		"/reorder_queue_peek_drop",
		test_reorder_queue_peek_drop,
		NULL,
		NULL,
		MUNIT_TEST_OPTION_NONE,
		NULL
	},
	{ NULL, NULL, NULL, NULL, MUNIT_TEST_OPTION_NONE, NULL }
};
